using Windows.Foundation;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Where quick settings sits: above the button that opened it, with the right edges aligned.</summary>
public static class QuickSettingsPlacement
{
    public const double Width = 360;

    public const double Gap = 8;

    public static Rect Above(Rect button, double height) =>
        new(button.Right - Width, button.Top - Gap - height, Width, height);
}
