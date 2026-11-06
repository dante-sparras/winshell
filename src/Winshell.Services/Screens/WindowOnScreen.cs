using Winshell.Native;

namespace Winshell;

/// <summary>A monitor's work area in physical pixels, and the scale that turns those pixels into logical ones.</summary>
internal readonly record struct MonitorFrame(int Left, int Top, int Right, int Bottom, double Scale)
{
    /// <summary>The work area a window is placed in, or the whole monitor when that area has no size.</summary>
    public static MonitorFrame Of(RECT bounds, RECT work, double scale)
    {
        var area = work.Width > 0 && work.Height > 0 ? work : bounds;
        return new MonitorFrame(area.Left, area.Top, area.Right, area.Bottom, scale);
    }
}

/// <summary>
/// A window about to be placed. <see cref="Shown"/> is the rectangle on screen.
/// <see cref="Restored"/> is the placement's rectangle, in workspace coordinates unless <see cref="ToolWindow"/> is set.
/// </summary>
internal readonly record struct WindowToMove(
    RECT Shown,
    RECT Restored,
    bool HasPlacement,
    bool Minimized,
    bool Maximized,
    bool ToolWindow);

/// <summary>
/// Where a window goes. <see cref="Screen"/> is screen pixels. <see cref="Workspace"/> is the restored rectangle a
/// placement stores, and equals <see cref="Screen"/> when the window has no placement.
/// </summary>
internal readonly record struct WindowPlace(RECT Screen, RECT Workspace);

/// <summary>
/// Places a window on a screen: the restored rectangle, workspace and screen coordinates, and the same logical size
/// and offset on another screen. Callers read the window and the monitors; this decides the rectangles.
/// </summary>
internal static class WindowOnScreen
{
    /// <summary>
    /// The restored rectangle in screen pixels. A normal window is already there. A minimized or maximized one is
    /// stored in workspace coordinates, which start at the primary monitor's work area, unless it is a tool window.
    /// The shown rectangle when the restored one has no size. A rectangle that hangs off the monitor is left there.
    /// </summary>
    public static RECT Restored(WindowToMove window, RECT primaryMonitor, RECT primaryWork)
    {
        var bias = Bias(window, primaryMonitor, primaryWork);
        return RectangleToMove(window.Minimized, window.Maximized, window.Shown,
            Shift(window.Restored, bias.X, bias.Y));
    }

    /// <summary>
    /// Where <paramref name="window"/> goes on <paramref name="to"/>, keeping its logical size and its offset in the
    /// work area. False when there is no rectangle to move. A window that would hang off the target is slid back,
    /// and one that is larger is shrunk to fit.
    /// </summary>
    public static bool TryMove(WindowToMove window, MonitorFrame from, MonitorFrame to, RECT primaryMonitor,
        RECT primaryWork, out WindowPlace place)
    {
        var source = Restored(window, primaryMonitor, primaryWork);
        if (source.Width <= 0 || source.Height <= 0)
        {
            place = default;
            return false;
        }

        var screen = MoveOnto(source, from, to);
        var bias = Bias(window, primaryMonitor, primaryWork);
        place = new WindowPlace(screen, window.HasPlacement ? Shift(screen, -bias.X, -bias.Y) : screen);
        return true;
    }

    /// <summary>
    /// How far the placement's rectangle is from screen coordinates. A window that is showing reports the gap.
    /// A minimized or maximized one cannot, so the gap is the primary work area, and a tool window has none.
    /// </summary>
    private static (int X, int Y) Bias(WindowToMove window, RECT primaryMonitor, RECT primaryWork)
    {
        if (window.HasPlacement && !window.Minimized && !window.Maximized)
            return (window.Shown.Left - window.Restored.Left, window.Shown.Top - window.Restored.Top);
        if (window.HasPlacement)
            return WorkspaceOffset(primaryMonitor, primaryWork, window.ToolWindow);
        return (0, 0);
    }

    /// <summary>A minimized or maximized window moves by its restored rectangle. A normal window moves by its current one.</summary>
    private static RECT RectangleToMove(bool minimized, bool maximized, RECT current, RECT restored)
    {
        if ((minimized || maximized) && restored.Width > 0 && restored.Height > 0)
            return restored;
        return current;
    }

    /// <summary>
    /// How far a window's restored rectangle is offset from screen coordinates. Windows reports it in workspace
    /// coordinates, which start at the primary monitor's work area, unless the window is a tool window.
    /// </summary>
    private static (int X, int Y) WorkspaceOffset(RECT primaryMonitor, RECT primaryWork, bool toolWindow)
    {
        return toolWindow || primaryWork.Width <= 0 || primaryWork.Height <= 0
            ? (0, 0)
            : (primaryWork.Left - primaryMonitor.Left, primaryWork.Top - primaryMonitor.Top);
    }

    private static RECT Shift(RECT rect, int x, int y)
    {
        return new RECT(rect.Left + x, rect.Top + y, rect.Right + x, rect.Bottom + y);
    }

    /// <summary>
    /// Where a window goes on another monitor: the same logical offset and size, in the target's physical pixels.
    /// A window that would hang off the target is slid back, and one that is larger is shrunk to fit. The size is
    /// rounded on its own, so it does not depend on where the window sits.
    /// </summary>
    private static RECT MoveOnto(RECT window, MonitorFrame from, MonitorFrame to)
    {
        if (from.Scale <= 0 || to.Scale <= 0 || from.Right <= from.Left || from.Bottom <= from.Top)
            return window;
        var width = (window.Right - window.Left) / from.Scale * to.Scale;
        var height = (window.Bottom - window.Top) / from.Scale * to.Scale;
        var left = to.Left + (window.Left - from.Left) / from.Scale * to.Scale;
        var top = to.Top + (window.Top - from.Top) / from.Scale * to.Scale;
        var targetWidth = to.Right - to.Left;
        var targetHeight = to.Bottom - to.Top;
        if (width > targetWidth)
            width = targetWidth;
        if (height > targetHeight)
            height = targetHeight;
        if (width < 1)
            width = 1;
        if (height < 1)
            height = 1;
        if (left < to.Left)
            left = to.Left;
        if (top < to.Top)
            top = to.Top;
        if (left + width > to.Right)
            left = to.Right - width;
        if (top + height > to.Bottom)
            top = to.Bottom - height;
        var x = (int)Math.Round(left);
        var y = (int)Math.Round(top);
        var w = Math.Max(1, (int)Math.Round(width));
        var h = Math.Max(1, (int)Math.Round(height));
        return new RECT(x, y, x + w, y + h);
    }
}
