using System.Runtime.InteropServices;

namespace Winshell.Windows;

/// <summary>
/// Which virtual desktop a window is on, and moving it to another, through the supported
/// <c>IVirtualDesktopManager</c>. Unlike the desktop list in <see cref="VirtualDesktopBroker"/>, it works on every build.
/// </summary>
internal static partial class WindowDesktop
{
    private const uint ClsctxInprocServer = 1;

    private static readonly Guid ManagerClass = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    private static IVirtualDesktopManager? _manager;
    private static bool _loggedUnavailable;

    /// <summary>The id of the desktop the window is on, or empty when Windows does not say.</summary>
    public static string IdOf(nint hwnd)
    {
        if (hwnd == 0)
            return "";
        try
        {
            var manager = Manager();
            if (manager is null)
                return "";
            if (manager.GetWindowDesktopId(hwnd, out var id) < 0 || id == Guid.Empty)
                return "";
            return id.ToString("D");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _manager = null;
            return "";
        }
    }

    public static bool Move(nint hwnd, Guid desktop)
    {
        if (hwnd == 0 || desktop == Guid.Empty)
            return false;
        try
        {
            var manager = Manager();
            if (manager is null)
                return false;
            var hr = manager.MoveWindowToDesktop(hwnd, in desktop);
            if (hr < 0)
            {
                Log.Default.Warn("desktops", $"MoveWindowToDesktop failed (0x{hr:X8}).");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _manager = null;
            Log.Default.Warn("desktops", $"MoveWindowToDesktop failed ({ex.GetType().Name}).");
            return false;
        }
    }

    private static IVirtualDesktopManager? Manager()
    {
        if (_manager is not null)
            return _manager;
        var iid = typeof(IVirtualDesktopManager).GUID;
        var hr = CoCreateInstance(in ManagerClass, 0, ClsctxInprocServer, in iid, out var pointer);
        if (hr < 0 || pointer == 0)
        {
            if (!_loggedUnavailable)
            {
                _loggedUnavailable = true;
                Log.Default.Warn("desktops", $"IVirtualDesktopManager is unavailable (0x{hr:X8}).");
            }
            return null;
        }
        try
        {
            _manager = (IVirtualDesktopManager)Marshal.GetObjectForIUnknown(pointer);
            return _manager;
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

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
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
