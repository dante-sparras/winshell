using Windows.Foundation;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Where Start sits: above the taskbar, centered on it or lined up with the Start button, then slid into the work area.</summary>
public static class StartPlacement
{
    /// <summary>The gap between the menu and the top of the taskbar.</summary>
    public const double Gap = 12;

    public static Rect Above(Rect taskbar, Rect workArea, bool centered, double startButtonX)
    {
        var width = Math.Min(StartList.MenuWidth, Math.Max(1, workArea.Width));
        var height = Math.Min(StartList.MenuHeight, Math.Max(1, workArea.Height - Gap));
        var anchorX = centered ? taskbar.X + taskbar.Width / 2 : startButtonX;
        var x = centered ? anchorX - width / 2 : anchorX;
        var y = taskbar.Y - Gap - height;
        if (x < workArea.X)
            x = workArea.X;
        else if (x + width > workArea.Right)
            x = workArea.Right - width;
        if (y < workArea.Y)
            y = workArea.Y;
        else if (y + height > workArea.Bottom)
            y = workArea.Bottom - height;
        return new Rect(Round(x), Round(y), Round(width), Round(height));
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}
