using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>Reads and changes virtual desktops. Tests use a stand-in; the shell uses <see cref="VirtualDesktopBroker"/>.</summary>
internal interface IVirtualDesktopSource
{
    DesktopContract Contract { get; }

    /// <summary>Changes when a desktop is added or removed. Reading it makes no call into Explorer.</summary>
    string ListStamp { get; }

    bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error);

    bool SwitchTo(Guid id);

    Guid? Create();

    bool Remove(Guid id, Guid fallback);
}

/// <summary>The build Windows reports, from <c>RtlGetVersion</c> rather than the version a manifest can change.</summary>
internal readonly record struct WindowsVersion(uint Build, uint Update)
{
    public override string ToString() => Update == 0 ? Build.ToString(CultureInfo.InvariantCulture) : $"{Build}.{Update}";

    public static WindowsVersion Current => _current ??= Read();

    private static WindowsVersion? _current;

    public static WindowsVersion Read()
    {
        var info = new OsVersionInfo { Size = (uint)Marshal.SizeOf<OsVersionInfo>() };
        if (RtlGetVersion(ref info) < 0)
            return new(0, 0);
        return new(info.Build, ReadUpdate());
    }

    private static uint ReadUpdate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("UBR") switch
            {
                int value when value > 0 => (uint)value,
                uint value => value,
                _ => 0,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        public uint Size;
        public uint Major;
        public uint Minor;
        public uint Build;
        public uint PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePack;
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfo info);
}

/// <summary>
/// Virtual desktops through the supported <c>IVirtualDesktopManager</c> and, on a known build, the internal
/// manager. The internal table gained <c>SwitchDesktopAndMoveForegroundView</c> in Windows 11 24H2 (build 26100);
/// 23H2 uses the same interface id and different slots, so <see cref="DesktopContract"/> refuses every other build.
/// A failed read never calls create, switch or remove.
/// </summary>
internal sealed partial class VirtualDesktopBroker : IVirtualDesktopSource
{
    public static VirtualDesktopBroker Shared { get; } = new();

    private const uint ClsctxInprocServer = 1;
    private const uint ClsctxLocalServer = 4;

    private static readonly Guid ManagerClass = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static readonly Guid ImmersiveShellClass = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid InternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    private IVirtualDesktopManager? _documented;
    private bool _loggedDocumented;

    public DesktopContract Contract
    {
        get
        {
            var version = WindowsVersion.Current;
            var contract = DesktopContract.ForBuild(version.Build);
            if (contract.Supported)
                return contract;
            return new(false,
                $"Windows {version} is outside the virtual desktop method table Winshell knows ({DesktopContract.OldestBuild} through {DesktopContract.NewestBuild}).");
        }
    }

    public static string WindowDesktopId(nint hwnd) => Shared.ReadWindowDesktopId(hwnd);

    public static bool MoveWindow(nint hwnd, Guid desktop) => Shared.MoveWindowTo(hwnd, desktop);

    public string ReadWindowDesktopId(nint hwnd)
    {
        if (hwnd == 0)
            return "";
        try
        {
            var manager = Documented();
            if (manager is null)
                return "";
            if (manager.GetWindowDesktopId(hwnd, out var id) < 0 || id == Guid.Empty)
                return "";
            return id.ToString("D");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _documented = null;
            return "";
        }
    }

    public bool MoveWindowTo(nint hwnd, Guid desktop)
    {
        if (hwnd == 0 || desktop == Guid.Empty)
            return false;
        try
        {
            var manager = Documented();
            if (manager is null)
                return false;
            var hr = manager.MoveWindowToDesktop(hwnd, in desktop);
            if (hr < 0)
            {
                Log.Default.Warn("desktops", $"MoveWindowToDesktop failed ({Hex(hr)}).");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _documented = null;
            Log.Default.Warn("desktops", $"MoveWindowToDesktop failed ({ex.GetType().Name}).");
            return false;
        }
    }

    public string ListStamp =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops", "VirtualDesktopIDs", null) is byte[] ids
            ? Convert.ToHexString(ids)
            : "";

    public bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error)
    {
        desktops = [];
        current = Guid.Empty;
        error = "";
        if (!TryInternal(out var manager, out error) || manager is null)
            return false;

        nint arrayPointer = 0;
        object? arrayObject = null;
        var held = new List<object>();
        try
        {
            var hr = manager.GetCount(out var count);
            if (hr < 0)
            {
                error = $"failed: GetCount {Hex(hr)}";
                return false;
            }
            if (count is 0 or > 128)
            {
                error = $"failed: GetCount returned {count}";
                return false;
            }

            hr = manager.GetDesktops(out arrayPointer);
            if (hr < 0 || arrayPointer == 0)
            {
                error = $"failed: GetDesktops {Hex(hr)}";
                return false;
            }
            arrayObject = Marshal.GetObjectForIUnknown(arrayPointer);
            var array = (IObjectArray)arrayObject;
            hr = array.GetCount(out var listed);
            if (hr < 0 || listed != count)
            {
                error = $"failed: the desktop list has {listed} entries after GetCount returned {count} ({Hex(hr)})";
                return false;
            }

            var iid = typeof(IVirtualDesktop).GUID;
            var records = new DesktopRecord[count];
            for (uint i = 0; i < count; i++)
            {
                hr = array.GetAt(i, in iid, out var item);
                if (hr < 0)
                {
                    error = $"failed: GetAt {i} {Hex(hr)}";
                    return false;
                }
                held.Add(item);
                var desktop = (IVirtualDesktop)item;
                hr = desktop.GetId(out var id);
                if (hr < 0 || id == Guid.Empty)
                {
                    error = $"failed: desktop {i} has no id ({Hex(hr)})";
                    return false;
                }
                records[i] = new DesktopRecord(id, ReadName(desktop));
            }

            hr = manager.GetCurrentDesktop(out var currentPointer);
            if (hr < 0 || currentPointer == 0)
            {
                error = $"failed: GetCurrentDesktop {Hex(hr)}";
                return false;
            }
            try
            {
                var currentObject = Marshal.GetObjectForIUnknown(currentPointer);
                held.Add(currentObject);
                hr = ((IVirtualDesktop)currentObject).GetId(out current);
            }
            finally
            {
                Marshal.Release(currentPointer);
            }
            if (hr < 0 || current == Guid.Empty)
            {
                error = $"failed: the current desktop has no id ({Hex(hr)})";
                current = Guid.Empty;
                return false;
            }

            desktops = records;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            error = $"failed: {ex.GetType().Name}";
            desktops = [];
            current = Guid.Empty;
            return false;
        }
        finally
        {
            if (arrayPointer != 0)
                Marshal.Release(arrayPointer);
            foreach (var item in held)
                Release(item);
            Release(arrayObject);
            Release(manager);
        }
    }

    public bool SwitchTo(Guid id)
    {
        if (!WithDesktop(id, out var manager, out var pointer) || manager is null)
            return false;
        try
        {
            var hr = manager.SwitchDesktop(pointer);
            if (hr < 0)
                Log.Default.Warn("desktops", $"SwitchDesktop failed ({Hex(hr)}).");
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            Log.Default.Warn("desktops", $"SwitchDesktop failed ({ex.GetType().Name}).");
            return false;
        }
        finally
        {
            Marshal.Release(pointer);
            Release(manager);
        }
    }

    public Guid? Create()
    {
        if (!TryInternal(out var manager, out var error) || manager is null)
        {
            Log.Default.Warn("desktops", $"CreateDesktop did not run ({error}).");
            return null;
        }
        nint pointer = 0;
        object? created = null;
        try
        {
            var hr = manager.CreateDesktop(out pointer);
            if (hr < 0 || pointer == 0)
            {
                Log.Default.Warn("desktops", $"CreateDesktop failed ({Hex(hr)}).");
                return null;
            }
            created = Marshal.GetObjectForIUnknown(pointer);
            hr = ((IVirtualDesktop)created).GetId(out var id);
            if (hr < 0 || id == Guid.Empty)
            {
                Log.Default.Warn("desktops", $"CreateDesktop returned a desktop with no id ({Hex(hr)}).");
                return null;
            }
            return id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            Log.Default.Warn("desktops", $"CreateDesktop failed ({ex.GetType().Name}).");
            return null;
        }
        finally
        {
            if (pointer != 0)
                Marshal.Release(pointer);
            Release(created);
            Release(manager);
        }
    }

    public bool Remove(Guid id, Guid fallback)
    {
        if (id == Guid.Empty || fallback == Guid.Empty || id == fallback)
            return false;
        if (!TryInternal(out var manager, out var error) || manager is null)
        {
            Log.Default.Warn("desktops", $"RemoveDesktop did not run ({error}).");
            return false;
        }
        nint destroy = 0, onto = 0;
        try
        {
            if (!TryFind(manager, id, out destroy) || !TryFind(manager, fallback, out onto))
            {
                Log.Default.Warn("desktops", "RemoveDesktop could not find the desktop or its fallback.");
                return false;
            }
            var hr = manager.RemoveDesktop(destroy, onto);
            if (hr < 0)
                Log.Default.Warn("desktops", $"RemoveDesktop failed ({Hex(hr)}).");
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            Log.Default.Warn("desktops", $"RemoveDesktop failed ({ex.GetType().Name}).");
            return false;
        }
        finally
        {
            if (destroy != 0)
                Marshal.Release(destroy);
            if (onto != 0)
                Marshal.Release(onto);
            Release(manager);
        }
    }

    private bool WithDesktop(Guid id, out IVirtualDesktopManagerInternal? manager, out nint pointer)
    {
        pointer = 0;
        if (!TryInternal(out manager, out var error) || manager is null)
        {
            Log.Default.Warn("desktops", $"SwitchDesktop did not run ({error}).");
            return false;
        }
        if (TryFind(manager, id, out pointer))
            return true;
        Release(manager);
        manager = null;
        Log.Default.Warn("desktops", "SwitchDesktop could not find that desktop.");
        return false;
    }

    private IVirtualDesktopManager? Documented()
    {
        if (_documented is not null)
            return _documented;
        var iid = typeof(IVirtualDesktopManager).GUID;
        var hr = CoCreateInstance(in ManagerClass, 0, ClsctxInprocServer, in iid, out var pointer);
        if (hr < 0 || pointer == 0)
        {
            if (!_loggedDocumented)
            {
                _loggedDocumented = true;
                Log.Default.Warn("desktops", $"IVirtualDesktopManager is unavailable ({Hex(hr)}).");
            }
            return null;
        }
        try
        {
            _documented = (IVirtualDesktopManager)Marshal.GetObjectForIUnknown(pointer);
            return _documented;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private static bool TryInternal(out IVirtualDesktopManagerInternal? manager, out string error)
    {
        manager = null;
        error = "";
        var providerId = typeof(IServiceProvider).GUID;
        var hr = CoCreateInstance(in ImmersiveShellClass, 0, ClsctxLocalServer, in providerId, out var shellPointer);
        if (hr < 0 || shellPointer == 0)
        {
            error = $"unavailable: immersive shell {Hex(hr)}";
            return false;
        }

        object? shellObject = null;
        nint managerPointer = 0;
        try
        {
            shellObject = Marshal.GetObjectForIUnknown(shellPointer);
            var shell = (IServiceProvider)shellObject;
            var iid = typeof(IVirtualDesktopManagerInternal).GUID;
            var service = InternalService;
            hr = shell.QueryService(in service, in iid, out managerPointer);
            if (hr < 0 || managerPointer == 0)
            {
                error = $"unavailable: virtual desktop service {Hex(hr)}";
                return false;
            }
            manager = (IVirtualDesktopManagerInternal)Marshal.GetObjectForIUnknown(managerPointer);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            error = $"unavailable: {ex.GetType().Name}";
            manager = null;
            return false;
        }
        finally
        {
            Marshal.Release(shellPointer);
            if (managerPointer != 0)
                Marshal.Release(managerPointer);
            Release(shellObject);
        }
    }

    private static bool TryFind(IVirtualDesktopManagerInternal manager, Guid id, out nint pointer)
    {
        pointer = 0;
        var hr = manager.GetDesktops(out var arrayPointer);
        if (hr < 0 || arrayPointer == 0)
            return false;
        object? arrayObject = null;
        var held = new List<object>();
        try
        {
            arrayObject = Marshal.GetObjectForIUnknown(arrayPointer);
            var array = (IObjectArray)arrayObject;
            if (array.GetCount(out var count) < 0 || count > 128)
                return false;
            var iid = typeof(IVirtualDesktop).GUID;
            for (uint i = 0; i < count; i++)
            {
                if (array.GetAt(i, in iid, out var item) < 0)
                    return false;
                held.Add(item);
                var desktop = (IVirtualDesktop)item;
                if (desktop.GetId(out var found) < 0 || found != id)
                    continue;
                pointer = Marshal.GetComInterfaceForObject(desktop, typeof(IVirtualDesktop));
                return true;
            }
            return false;
        }
        finally
        {
            Marshal.Release(arrayPointer);
            foreach (var item in held)
                Release(item);
            Release(arrayObject);
        }
    }

    private static string ReadName(IVirtualDesktop desktop)
    {
        if (desktop.GetName(out var name) < 0 || name == 0)
            return "";
        try
        {
            return Marshal.PtrToStringUni(WindowsGetStringRawBuffer(name, out _)) ?? "";
        }
        finally
        {
            Win32.WindowsDeleteString(name);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.ReleaseComObject(com);
    }

    private static string Hex(int hr) => $"0x{hr:X8}";

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("combase.dll")]
    private static partial nint WindowsGetStringRawBuffer(nint hstring, out uint length);
}

[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, out int onCurrent);
    [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid desktopId);
    [PreserveSig] int MoveWindowToDesktop(nint hwnd, in Guid desktopId);
}

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    [PreserveSig] int QueryService(in Guid service, in Guid iid, out nint instance);
}

/// <summary>
/// The internal desktop manager on builds 26100 through 26399. Slots Windows inserted in 24H2 stay, including
/// <c>MoveDesktop</c>, which is not called: removing it would move <c>RemoveDesktop</c> onto the wrong slot.
/// </summary>
[ComImport]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManagerInternal
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int MoveViewToDesktop(nint view, nint desktop);
    [PreserveSig] int CanViewMoveDesktops(nint view, out int canMove);
    [PreserveSig] int GetCurrentDesktop(out nint desktop);
    [PreserveSig] int GetDesktops(out nint desktops);
    [PreserveSig] int GetAdjacentDesktop(nint desktop, int direction, out nint adjacent);
    [PreserveSig] int SwitchDesktop(nint desktop);
    [PreserveSig] int SwitchDesktopAndMoveForegroundView(nint desktop);
    [PreserveSig] int CreateDesktop(out nint desktop);
    [PreserveSig] int MoveDesktop(nint desktop, int index);
    [PreserveSig] int RemoveDesktop(nint destroy, nint fallback);
}

[ComImport]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktop
{
    [PreserveSig] int IsViewVisible(nint view, out int visible);
    [PreserveSig] int GetId(out Guid id);
    [PreserveSig] int GetName(out nint name);
}

[ComImport]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
}
