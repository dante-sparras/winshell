using Windows.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;

namespace Winshell;

/// <summary>Where a popup sits, in logical pixels on the virtual desktop.</summary>
internal sealed record PopupPlacement(ScreenInfo? Screen, Rect Rect);

/// <summary>Puts a popup where it was asked, at the scale of the screen it is on, above other windows.</summary>
internal sealed class PopupPlacer(HostWindow host, ShellPlatform platform) : IWindowPlacer
{
    private PopupPlacement? _placement;

    internal void Set(PopupPlacement placement)
    {
        _placement = placement;
        // The rect is relative to the parent window's screen, so it maps back through that screen's scale.
        var monitors = Screens.List();
        var at = DesktopSpace.MonitorAt(new Point(placement.Rect.X, placement.Rect.Y),
            monitors.Select(m => m.Logical).ToList());
        var monitor = monitors.FirstOrDefault(m => m.Id == placement.Screen?.Id) ??
                      (at is { } i ? monitors[i] : Screens.Primary());
        if (monitor is null)
            return;
        host.Placement.Scale = monitor.Scale;
        var r = DesktopSpace.ToPhysical(placement.Rect, monitor.Bounds, monitor.Scale);
        host.Placement.Move(r, Win32.HWND_TOPMOST,
            (uint)(SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER));
    }

    /// <summary>A popup only changes DPI on its own when its monitor's scale changes: place it again from its logical size.</summary>
    public void OnDpiChanged(RECT suggested, bool placing)
    {
        if (!placing)
            platform.Loop.Post(Replace);
    }

    private void Replace()
    {
        if (host.Handle != 0 && _placement is { } placement)
            Set(placement);
    }
}
