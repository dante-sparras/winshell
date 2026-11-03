using Windows.Foundation;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Where the notification center and calendar opens: above the clock, inside the work area.</summary>
public static class CalendarFlyoutPlacement
{
    /// <summary>
    /// Right edges aligned, eight pixels above the clock. 384 wide and at most 780 tall, and no taller than the work
    /// area minus 16. A rectangle that would leave the work area slides back in.
    /// </summary>
    public static Rect Place(Rect clock, Rect workArea)
    {
        var width = Math.Min(384, workArea.Width);
        var height = Math.Min(780, Math.Max(0, workArea.Height - 16));
        var x = Math.Max(workArea.X, Math.Min(clock.Right - width, workArea.Right - width));
        var y = Math.Max(workArea.Y, Math.Min(clock.Y - 8 - height, workArea.Bottom - height));
        return new Rect(Math.Round(x), Math.Round(y), Math.Round(width), Math.Round(height));
    }
}
