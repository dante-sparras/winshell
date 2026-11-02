namespace Winshell.DefaultShell.Taskbar;

/// <summary>Where the notification center and calendar opens: above the clock, inside the work area.</summary>
public static class CalendarFlyoutPlacement
{
    /// <summary>
    /// Right edges aligned, eight pixels above the clock. 384 wide and at most 780 tall, and no taller than the work
    /// area minus 16. A rectangle that would leave the work area slides back in.
    /// </summary>
    public static QRect Place(QRect clock, QRect workArea)
    {
        var width = Math.Min(384, workArea.Width);
        var height = Math.Min(780, workArea.Height - 16);
        var x = Math.Max(workArea.X, Math.Min(clock.Right - width, workArea.Right - width));
        var y = Math.Max(workArea.Y, Math.Min(clock.Y - 8 - height, workArea.Bottom - height));
        return new QRect(Math.Round(x), Math.Round(y), Math.Round(width), Math.Round(height));
    }
}
