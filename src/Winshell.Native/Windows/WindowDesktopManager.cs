using System.Runtime.InteropServices;
using Windows.Win32.System.Com;

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

    private WindowDesktopManager(IVirtualDesktopManager manager)
    {
        _manager = manager;
    }

    /// <summary>The manager, or null. <paramref name="hr"/> is negative when Windows could not create it.</summary>
    internal static WindowDesktopManager? Create(out int hr)
    {
        var manager = Com.Create<IVirtualDesktopManager>(ManagerClass, CLSCTX.CLSCTX_INPROC_SERVER, out hr);
        return manager is null ? null : new WindowDesktopManager(manager);
    }

    /// <summary>The HRESULT of asking which desktop the window is on.</summary>
    internal int DesktopOf(nint hwnd, out Guid desktop)
    {
        return _manager.GetWindowDesktopId(hwnd, out desktop);
    }

    /// <summary>The HRESULT of moving the window to that desktop.</summary>
    internal int Move(nint hwnd, Guid desktop)
    {
        return _manager.MoveWindowToDesktop(hwnd, in desktop);
    }
}
