using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.WinRT;

namespace Winshell.Native;

/// <summary>A virtual desktop: its id, and the name the user gave it or empty.</summary>
internal readonly record struct VirtualDesktopEntry(Guid Id, string Name);

/// <summary>
/// The virtual desktops through Explorer's internal manager, reached through the immersive shell. Its method table
/// changes between Windows builds, so the caller checks the build first. Each call takes the manager fresh and
/// releases every object before it returns. A failed step goes no further and says what failed.
/// </summary>
internal static class ImmersiveDesktops
{
    private static readonly Guid ImmersiveShellClass = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid InternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    /// <summary>More desktops than this is a broken answer, not a list to read.</summary>
    private const uint MaxDesktops = 128;

    /// <summary>
    /// Every desktop in order, and the current one. <paramref name="error"/> starts with <c>unavailable:</c> when the
    /// manager cannot be reached, and with <c>failed:</c> when it gave a broken answer.
    /// </summary>
    internal static bool TryRead(out IReadOnlyList<VirtualDesktopEntry> desktops, out Guid current, out string error)
    {
        desktops = [];
        current = Guid.Empty;
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

            if (count is 0 or > MaxDesktops)
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
            var entries = new VirtualDesktopEntry[count];
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

                entries[i] = new VirtualDesktopEntry(id, ReadName(desktop));
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

            desktops = entries;
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

    /// <summary>Switches to the desktop with that id. <paramref name="problem"/> says why not, after the method's name.</summary>
    internal static bool SwitchTo(Guid id, out string problem)
    {
        if (!TryInternal(out var manager, out var error) || manager is null)
        {
            problem = $"did not run ({error})";
            return false;
        }

        nint pointer = 0;
        try
        {
            if (!TryFind(manager, id, out pointer))
            {
                problem = "could not find that desktop";
                return false;
            }

            var hr = manager.SwitchDesktop(pointer);
            problem = hr < 0 ? $"failed ({Hex(hr)})" : "";
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            problem = $"failed ({ex.GetType().Name})";
            return false;
        }
        finally
        {
            if (pointer != 0)
                Marshal.Release(pointer);
            Release(manager);
        }
    }

    /// <summary>Adds a desktop and returns its id, or null. <paramref name="problem"/> says why not, after the method's name.</summary>
    internal static Guid? Create(out string problem)
    {
        if (!TryInternal(out var manager, out var error) || manager is null)
        {
            problem = $"did not run ({error})";
            return null;
        }

        nint pointer = 0;
        object? created = null;
        try
        {
            var hr = manager.CreateDesktop(out pointer);
            if (hr < 0 || pointer == 0)
            {
                problem = $"failed ({Hex(hr)})";
                return null;
            }

            created = Marshal.GetObjectForIUnknown(pointer);
            hr = ((IVirtualDesktop)created).GetId(out var id);
            if (hr < 0 || id == Guid.Empty)
            {
                problem = $"returned a desktop with no id ({Hex(hr)})";
                return null;
            }

            problem = "";
            return id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            problem = $"failed ({ex.GetType().Name})";
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

    /// <summary>
    /// Removes the desktop <paramref name="id"/>, moving its windows to <paramref name="fallback"/>.
    /// <paramref name="problem"/> says why not, after the method's name.
    /// </summary>
    internal static bool Remove(Guid id, Guid fallback, out string problem)
    {
        if (!TryInternal(out var manager, out var error) || manager is null)
        {
            problem = $"did not run ({error})";
            return false;
        }

        nint destroy = 0, onto = 0;
        try
        {
            if (!TryFind(manager, id, out destroy) || !TryFind(manager, fallback, out onto))
            {
                problem = "could not find the desktop or its fallback";
                return false;
            }

            var hr = manager.RemoveDesktop(destroy, onto);
            problem = hr < 0 ? $"failed ({Hex(hr)})" : "";
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            problem = $"failed ({ex.GetType().Name})";
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

    private static unsafe bool TryInternal(out IVirtualDesktopManagerInternal? manager, out string error)
    {
        manager = null;
        error = "";
        var clsid = ImmersiveShellClass;
        var providerId = typeof(IServiceProvider).GUID;
        void* created;
        var hr = PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_LOCAL_SERVER, &providerId, &created).Value;
        var shellPointer = (nint)created;
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

    /// <summary>The desktop with that id, as an <c>IVirtualDesktop</c> pointer with one reference the caller releases.</summary>
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
            if (array.GetCount(out var count) < 0 || count > MaxDesktops)
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

    private static unsafe string ReadName(IVirtualDesktop desktop)
    {
        if (desktop.GetName(out var name) < 0 || name == 0)
            return "";
        try
        {
            var text = PInvoke.WindowsGetStringRawBuffer(new HSTRING((void*)name), out var length);
            return text.Value is null ? "" : new string(text.Value, 0, (int)length);
        }
        finally
        {
            _ = PInvoke.WindowsDeleteString(new HSTRING((void*)name));
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.ReleaseComObject(com);
    }

    private static string Hex(int hr)
    {
        return $"0x{hr:X8}";
    }
}
