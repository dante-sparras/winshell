using Winshell.Native;

namespace Winshell;

/// <summary>
/// Maps between the physical pixels of the virtual desktop and the logical pixels QML sees. As Qt does on Windows,
/// a monitor keeps its physical top-left corner and divides its size by its scale, so monitors of mixed DPI never
/// overlap: every logical point belongs to at most one monitor, and maps back through that monitor's scale.
/// </summary>
internal static class DesktopSpace
{
    public static double Scale(uint dpi) => dpi == 0 ? 1 : dpi / 96.0;

    /// <summary>A physical rectangle on a monitor, in logical pixels.</summary>
    public static QRect ToLogical(RECT rect, RECT monitor, double scale)
    {
        var origin = ToLogical(new POINT { X = rect.Left, Y = rect.Top }, monitor, scale);
        return new QRect(origin.X, origin.Y, rect.Width / scale, rect.Height / scale);
    }

    public static QPoint ToLogical(POINT point, RECT monitor, double scale) =>
        new(monitor.Left + (point.X - monitor.Left) / scale, monitor.Top + (point.Y - monitor.Top) / scale);

    public static POINT ToPhysical(QPoint point, RECT monitor, double scale) => new()
    {
        X = (int)Math.Round(monitor.Left + (point.X - monitor.Left) * scale),
        Y = (int)Math.Round(monitor.Top + (point.Y - monitor.Top) * scale),
    };

    /// <summary>A logical rectangle on a monitor, in physical pixels. The size is rounded on its own, so it does not depend on the position.</summary>
    public static RECT ToPhysical(QRect rect, RECT monitor, double scale)
    {
        var origin = ToPhysical(new QPoint(rect.X, rect.Y), monitor, scale);
        return new RECT(origin.X, origin.Y, origin.X + (int)Math.Round(rect.Width * scale), origin.Y + (int)Math.Round(rect.Height * scale));
    }

    /// <summary>The monitor a logical point is on, or null when it is in no monitor, as in the gap beside a scaled one.</summary>
    public static int? MonitorAt(QPoint point, IReadOnlyList<QRect> monitors)
    {
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            if (point.X >= m.X && point.X < m.Right && point.Y >= m.Y && point.Y < m.Bottom)
                return i;
        }
        return null;
    }

    /// <summary>Where a panel sits inside an area of its monitor, in physical pixels: anchored edges stick to the area, margins and size scale.</summary>
    public static RECT Anchor(RECT area, PanelPlacement p, double scale)
    {
        var ml = (int)Math.Round(p.MarginLeft * scale);
        var mr = (int)Math.Round(p.MarginRight * scale);
        var mt = (int)Math.Round(p.MarginTop * scale);
        var mb = (int)Math.Round(p.MarginBottom * scale);
        var w = (int)Math.Round(p.Width * scale);
        var h = (int)Math.Round(p.Height * scale);
        var (x, width) = Span(area.Left, area.Width, p.Anchors.HasFlag(Edges.Left), p.Anchors.HasFlag(Edges.Right), ml, mr, w);
        var (y, height) = Span(area.Top, area.Height, p.Anchors.HasFlag(Edges.Top), p.Anchors.HasFlag(Edges.Bottom), mt, mb, h);
        return new RECT(x, y, x + Math.Max(1, width), y + Math.Max(1, height));
    }

    private static (int Start, int Length) Span(int start, int length, bool near, bool far, int nearMargin, int farMargin, int size)
    {
        if (near && far)
            return (start + nearMargin, length - nearMargin - farMargin);
        if (near)
            return (start + nearMargin, size);
        if (far)
            return (start + length - size - farMargin, size);
        return (start + (length - size) / 2, size);
    }
}
