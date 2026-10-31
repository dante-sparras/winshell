namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>MenuHandle</c>: a menu that QML can open. A service subclasses it and overrides <see cref="Display"/>
/// to show the menu natively. A tray icon's menu belongs to its app, which draws it itself.
/// </summary>
public class MenuHandle : QObject
{
    /// <summary>Shows the menu as a platform menu at a point relative to a window, or under the pointer.</summary>
    public virtual void Display(ShellWindow? window, double x, double y)
    {
    }

    /// <summary>The point on the virtual desktop, in physical pixels, of a point relative to a window.</summary>
    public static (int X, int Y)? ScreenPoint(ShellWindow? window, double x, double y) => window?.Backend?.MapToScreen(x, y);
}

/// <summary>The <c>MenuButtonType</c>.</summary>
public enum MenuButtonType
{
    None = 0,
    CheckBox = 1,
    RadioButton = 2,
}
