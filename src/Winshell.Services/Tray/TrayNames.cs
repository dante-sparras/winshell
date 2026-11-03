namespace Winshell.Tray;

/// <summary>
/// The window class the host registers, the taskbar class it passes messages on to, and the name that makes one
/// host in the session the owner. Tests use their own, so they never see or disturb the real notification area.
/// </summary>
internal sealed record TrayNames(string Window, string Explorer, string Owner)
{
    public static readonly TrayNames Session = new("Shell_TrayWnd", "Shell_TrayWnd", @"Local\winshell-notification-area");
}
