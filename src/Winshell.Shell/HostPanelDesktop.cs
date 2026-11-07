using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;
using Winshell.Tray;

namespace Winshell;

/// <summary>Carries out <see cref="PanelEdge"/>'s answers on the monitors, Explorer's app bar, and the panel's window.</summary>
internal sealed class HostPanelDesktop(HostWindow host, ShellPlatform platform) : IPanelDesktop
{
    public PanelMonitor? Find(string id)
    {
        return Screens.Find(id) is { } monitor ? Of(monitor) : null;
    }

    public PanelMonitor? Primary()
    {
        var id = platform.PrimaryScreenId;
        return id.Length == 0 ? null : Find(id);
    }

    public bool FullscreenOn(string screenId)
    {
        return platform.FullscreenOn(screenId);
    }

    public IDisposable Explorer()
    {
        return SystemTray.ExplorerFirst();
    }

    public bool Register(uint callback)
    {
        return AppBars.Register(host.Handle, callback);
    }

    public RECT Query(AppBarEdge edge, RECT proposed)
    {
        return AppBars.Query(host.Handle, edge, proposed);
    }

    public RECT Reserve(AppBarEdge edge, RECT desired)
    {
        return AppBars.Reserve(host.Handle, edge, desired);
    }

    public void Remove()
    {
        AppBars.Remove(host.Handle);
    }

    public void Move(RECT rect, bool topmost, double scale)
    {
        // UpdateSize runs inside the move and divides by this. A DPI change during the move keeps it too.
        host.Placement.Scale = scale;
        host.Placement.SetExStyle((uint)WINDOW_EX_STYLE.WS_EX_TOPMOST, topmost);
        // HWND_NOTOPMOST would put it above every window that is not topmost, so over the fullscreen app.
        var after = topmost ? Win32.HWND_TOPMOST : Win32.HWND_BOTTOM;
        host.Placement.Move(rect, after,
            (uint)(SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER));
    }

    public void WorkAreaMayHaveChanged()
    {
        platform.WorkAreaMayHaveChanged();
    }

    public void Post(Action action)
    {
        platform.Loop.Post(action);
    }

    private static PanelMonitor Of(ConnectedMonitor monitor)
    {
        return new PanelMonitor(monitor.Info.Id, monitor.Bounds, monitor.Area, monitor.Scale);
    }
}
