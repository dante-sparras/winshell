using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// The supported <c>IVirtualDesktopManager</c>: which virtual desktop a window is on, and moving it to another. It
/// works on every build. A call can throw <see cref="COMException"/>, <see cref="InvalidCastException"/> or
/// <see cref="InvalidComObjectException"/> once the manager is broken; the owner then makes a new one.
/// </summary>
internal sealed class WindowDesktopManager
{
    private static readonly Guid ManagerClass = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    private readonly IVirtualDesktopManager _manager;

    private WindowDesktopManager(IVirtualDesktopManager manager) => _manager = manager;

    /// <summary>The manager, or null. <paramref name="hr"/> is negative when Windows could not create it.</summary>
    internal static WindowDesktopManager? Create(out int hr)
    {
        var iid = typeof(IVirtualDesktopManager).GUID;
        hr = Win32.CoCreateInstance(in ManagerClass, 0, Win32.CLSCTX_INPROC_SERVER, in iid, out var pointer);
        if (hr < 0 || pointer == 0)
            return null;
        try
        {
            return new WindowDesktopManager((IVirtualDesktopManager)Marshal.GetObjectForIUnknown(pointer));
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

    /// <summary>The HRESULT of asking which desktop the window is on.</summary>
    internal int DesktopOf(nint hwnd, out Guid desktop) => _manager.GetWindowDesktopId(hwnd, out desktop);

    /// <summary>The HRESULT of moving the window to that desktop.</summary>
    internal int Move(nint hwnd, Guid desktop) => _manager.MoveWindowToDesktop(hwnd, in desktop);
}
