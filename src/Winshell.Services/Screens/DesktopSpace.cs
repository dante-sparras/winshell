using Windows.Foundation;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// Maps between the physical pixels of the virtual desktop and logical pixels. On Windows,
/// a monitor keeps its physical top-left corner and divides its size by its scale, so monitors of mixed DPI never
/// overlap: every logical point belongs to at most one monitor, and maps back through that monitor's scale.
/// </summary>
internal static class DesktopSpace
{
    public static double Scale(uint dpi)
    {
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    /// <summary>A physical rectangle on a monitor, in logical pixels.</summary>
    public static Rect ToLogical(RECT rect, RECT monitor, double scale)
    {
        var origin = ToLogical(new POINT { X = rect.Left, Y = rect.Top }, monitor, scale);
        return new Rect(origin.X, origin.Y, Math.Max(0, rect.Width) / scale, Math.Max(0, rect.Height) / scale);
    }

    public static Point ToLogical(POINT point, RECT monitor, double scale)
    {
        return new Point(monitor.Left + (point.X - monitor.Left) / scale,
            monitor.Top + (point.Y - monitor.Top) / scale);
    }

    public static POINT ToPhysical(Point point, RECT monitor, double scale)
    {
        return new POINT
        {
            X = (int)Math.Round(monitor.Left + (point.X - monitor.Left) * scale),
            Y = (int)Math.Round(monitor.Top + (point.Y - monitor.Top) * scale)
        };
    }

    /// <summary>A logical rectangle on a monitor, in physical pixels. The size is rounded on its own, so it does not depend on the position.</summary>
    public static RECT ToPhysical(Rect rect, RECT monitor, double scale)
    {
        var origin = ToPhysical(new Point(rect.X, rect.Y), monitor, scale);
        return new RECT(origin.X, origin.Y, origin.X + (int)Math.Round(rect.Width * scale),
            origin.Y + (int)Math.Round(rect.Height * scale));
    }

    /// <summary>The monitor a logical point is on, or null when it is in no monitor, as in the gap beside a scaled one.</summary>
    public static int? MonitorAt(Point point, IReadOnlyList<Rect> monitors)
    {
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            if (point.X >= m.X && point.X < m.Right && point.Y >= m.Y && point.Y < m.Bottom)
                return i;
        }

        return null;
    }
}
