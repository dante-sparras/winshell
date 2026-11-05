namespace Winshell.Native;

/// <summary>
/// The supported <c>IVirtualDesktopManager</c>: which virtual desktop a window is on, and moving it to another. It
/// works on every build. It holds one reference for the process; call it on the thread that created it.
/// </summary>
internal sealed unsafe class WindowDesktopManager
{
    private static readonly Guid ManagerClass = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static readonly Guid ManagerId = new("A5CD92FF-29BE-454C-8D04-D82879FB3F1B");

    // IVirtualDesktopManager's slots, after IUnknown's three.
    private const int GetWindowDesktopIdSlot = 4;
    private const int MoveWindowToDesktopSlot = 5;

    private readonly void* _manager;

    private WindowDesktopManager(void* manager) => _manager = manager;

    /// <summary>The manager, or null with the HRESULT of creating it.</summary>
    internal static WindowDesktopManager? Create(out int hr)
    {
        hr = Win32.CoCreateInstance(in ManagerClass, 0, Win32.CLSCTX_INPROC_SERVER, in ManagerId, out var pointer);
        return hr < 0 || pointer == 0 ? null : new WindowDesktopManager((void*)pointer);
    }

    /// <summary>The HRESULT of asking which desktop the window is on.</summary>
    internal int DesktopOf(nint hwnd, out Guid desktop)
    {
        Guid id;
        var hr = ComSlot.Call(_manager, GetWindowDesktopIdSlot, hwnd, &id);
        desktop = hr < 0 ? Guid.Empty : id;
        return hr;
    }

    /// <summary>The HRESULT of moving the window to that desktop.</summary>
    internal int Move(nint hwnd, Guid desktop) => ComSlot.Call(_manager, MoveWindowToDesktopSlot, hwnd, &desktop);
}
