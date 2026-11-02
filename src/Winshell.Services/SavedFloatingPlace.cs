using Winshell.Native;

namespace Winshell;

/// <summary>
/// The framed place a fullscreen window returns to. The offset and size are logical pixels from the monitor's
/// physical corner, so a later scale or a moved monitor still lands in the same place on that monitor.
/// </summary>
internal sealed record SavedFloatingPlace(
    uint Style,
    double Scale,
    RECT MonitorBounds,
    string MonitorId,
    double OffsetX,
    double OffsetY,
    double Width,
    double Height,
    bool Maximized,
    bool Minimized,
    Winshell.Native.WINDOWPLACEMENT Placement,
    bool HasPlacement)
{
    /// <summary>True when this place can be put back with the saved placement, because the monitor and its scale are unchanged.</summary>
    public bool Matches(string monitorId, RECT bounds, double scale) =>
        monitorId == MonitorId
        && bounds.Left == MonitorBounds.Left
        && bounds.Top == MonitorBounds.Top
        && bounds.Right == MonitorBounds.Right
        && bounds.Bottom == MonitorBounds.Bottom
        && Math.Abs(scale - Scale) < 0.001;

    /// <summary>The saved place on a monitor, in that monitor's physical pixels at <paramref name="scale"/>.</summary>
    public RECT RectOn(RECT monitor, double scale)
    {
        var logical = new QRect(monitor.Left + OffsetX, monitor.Top + OffsetY, Width, Height);
        return DesktopSpace.ToPhysical(logical, monitor, scale);
    }
}
