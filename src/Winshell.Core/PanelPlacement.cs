
namespace Winshell.Core;

/// <summary>A rectangle in physical pixels.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>The panel's top-left, in physical pixels.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
public readonly record struct PanelOrigin(int X, int Y);

/// <summary>Puts a panel beside the bar, with a gap, centered on the item that opened it.</summary>
public static class PanelPlacement
{
    /// <summary>Device-independent pixels between the bar and every panel, and between a panel and the screen edge.</summary>
    public const int Gap = 8;

    /// <summary>
    /// Where a panel of <paramref name="panelWidth"/> by <paramref name="panelHeight"/> should open.
    /// A top or bottom bar centers the panel on <paramref name="item"/> and leaves <paramref name="gap"/> beyond the bar's outer edge.
    /// A left or right bar centers the panel on the item's height and leaves that same gap.
    /// An item with no size along that axis keeps the panel's near edge on the item's near edge.
    /// The panel stays inside <paramref name="screen"/>, <paramref name="gap"/> from its edges.
    /// A panel too large for the room inside the gaps starts at the gap.
    /// </summary>
    /// <param name="edge">Screen edge the bar occupies.</param>
    /// <param name="bar">The bar window.</param>
    /// <param name="item">The item that opened the panel.</param>
    /// <param name="panelWidth">Panel width.</param>
    /// <param name="panelHeight">Panel height.</param>
    /// <param name="screen">The monitor that contains the bar.</param>
    /// <param name="gap">Pixels between the bar and the panel, and between the panel and the screen edge, in the same space as the rectangles.</param>
    /// <returns>The panel's top-left.</returns>
    public static PanelOrigin Beside(BarEdge edge, PixelRect bar, PixelRect item, int panelWidth, int panelHeight, PixelRect screen, int gap)
    {
        var space = Math.Max(0, gap);
        var alongBar = edge is BarEdge.Left or BarEdge.Right;
        var x = alongBar
            ? edge == BarEdge.Left ? bar.X + bar.Width + space : bar.X - panelWidth - space
            : Center(item.X, item.Width, panelWidth);
        var y = alongBar
            ? Center(item.Y, item.Height, panelHeight)
            : edge == BarEdge.Bottom ? bar.Y - panelHeight - space : bar.Y + bar.Height + space;
        return new PanelOrigin(
            Fit(x, screen.X, screen.Width, panelWidth, space),
            Fit(y, screen.Y, screen.Height, panelHeight, space));
    }

    /// <summary>
    /// The largest panel <see cref="Beside"/> leaves beside the bar: away from the bar, the room between the bar and the far edge of
    /// <paramref name="screen"/>, and along the bar, the screen itself, each less <paramref name="gap"/> at both ends.
    /// A larger panel stays inside the screen by covering the bar.
    /// A bar that is not on the screen takes no room.
    /// </summary>
    /// <param name="edge">Screen edge the bar occupies.</param>
    /// <param name="bar">The bar window.</param>
    /// <param name="screen">The monitor that contains the bar.</param>
    /// <param name="gap">Pixels between the bar and the panel, and between the panel and the screen edge, in the same space as the rectangles.</param>
    /// <returns>The largest width and height. Neither is less than the gap, however little room the bar leaves.</returns>
    public static (int Width, int Height) Largest(BarEdge edge, PixelRect bar, PixelRect screen, int gap)
    {
        var space = Math.Max(0, gap);
        var left = screen.X + space;
        var top = screen.Y + space;
        var right = screen.X + screen.Width - space;
        var bottom = screen.Y + screen.Height - space;
        if (Overlaps(bar, screen))
        {
            switch (edge)
            {
                case BarEdge.Left:
                    left = Math.Max(left, bar.X + bar.Width + space);
                    break;
                case BarEdge.Right:
                    right = Math.Min(right, bar.X - space);
                    break;
                case BarEdge.Bottom:
                    bottom = Math.Min(bottom, bar.Y - space);
                    break;
                default:
                    top = Math.Max(top, bar.Y + bar.Height + space);
                    break;
            }
        }

        return (Math.Max(space, right - left), Math.Max(space, bottom - top));
    }

    private static bool Overlaps(PixelRect one, PixelRect other) =>
        one.X < other.X + other.Width && other.X < one.X + one.Width && one.Y < other.Y + other.Height && other.Y < one.Y + one.Height;

    /// <summary>
    /// Where a panel that belongs to no place on the bar opens, such as the Menu: centered across <paramref name="screen"/>,
    /// with a third of the room that is left over above it, so its first line sits where the eye already is.
    /// The panel stays inside the screen, <paramref name="gap"/> from its edges.
    /// A panel that grows while it is open is placed as one <paramref name="room"/> tall, so its first line stays where it is as it grows.
    /// </summary>
    /// <param name="screen">The monitor that contains the bar.</param>
    /// <param name="panelWidth">Panel width.</param>
    /// <param name="panelHeight">Panel height.</param>
    /// <param name="gap">Pixels between the panel and the screen edge, in the same space as the rectangle.</param>
    /// <param name="room">The height the panel grows to, in the same space as the rectangle. Zero for a panel that keeps its height.</param>
    /// <returns>The panel's top-left.</returns>
    public static PanelOrigin Middle(PixelRect screen, int panelWidth, int panelHeight, int gap, int room = 0)
    {
        var space = Math.Max(0, gap);
        var height = Math.Max(panelHeight, room);
        var x = screen.X + ((screen.Width - panelWidth) / 2);
        var y = screen.Y + ((screen.Height - height) / 3);
        return new PanelOrigin(
            Fit(x, screen.X, screen.Width, panelWidth, space),
            Fit(y, screen.Y, screen.Height, height, space));
    }

    private static int Center(int start, int span, int panel)
    {
        if (span <= 0)
        {
            return start;
        }

        return start + (span / 2) - (panel / 2);
    }

    private static int Fit(int value, int screenStart, int screenSpan, int panel, int inset)
    {
        var start = screenStart + inset;
        var room = Math.Max(0, screenSpan - (inset * 2) - Math.Max(0, panel));
        return Math.Clamp(value, start, start + room);
    }

    /// <summary>The shortest a panel's rows are taken to be, in device-independent pixels: one line of text. The shortest panel is this and its padding.</summary>
    public const int MinimumRow = 16;

    /// <summary>The tallest panel, in device-independent pixels. Taller content scrolls.</summary>
    public const int MaximumHeight = 640;

    /// <summary>
    /// The height a panel takes for content measured at <paramref name="content"/> high inside <paramref name="chrome"/> of padding and border.
    /// Null when the content has not been measured, so the caller falls back to its estimate.
    /// </summary>
    /// <param name="content">Measured content height, in device-independent pixels.</param>
    /// <param name="chrome">Padding and border above and below the content. Zero for a panel with no padding.</param>
    /// <returns>The panel height, rounded up and kept between <see cref="MinimumRow"/> inside that chrome and <see cref="MaximumHeight"/>.</returns>
    public static int? FitHeight(double content, double chrome)
    {
        if (double.IsNaN(content) || content <= 0)
        {
            return null;
        }

        var around = Math.Max(0, chrome);
        var shortest = Math.Min(Math.Ceiling(MinimumRow + around), MaximumHeight);
        return (int)Math.Clamp(Math.Ceiling(content + around), shortest, MaximumHeight);
    }

    /// <summary>The widest outline a panel window is taken to have along one axis, in physical pixels.</summary>
    public const int MaximumOutline = 16;

    /// <summary>
    /// The pixels of a panel window along one axis that are outside its client area: the outline Windows draws.
    /// A panel's size is the size its rows fill, so the window is this much larger, and the padding stays the same on both sides.
    /// </summary>
    /// <param name="window">The window's width or height, in physical pixels.</param>
    /// <param name="client">The client area's width or height, in physical pixels.</param>
    /// <returns>The outline on both sides together. Zero when the window has no client area yet, or the two do not describe an outline.</returns>
    public static int Outline(int window, int client)
    {
        var outline = window - client;
        return client <= 0 || outline < 0 || outline > MaximumOutline ? 0 : outline;
    }
}
