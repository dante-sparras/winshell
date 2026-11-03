namespace Winshell.Tray;

/// <summary>What the public tray needs of the host. A test gives it a fake, and nothing touches the real tray.</summary>
internal interface ITrayHost
{
    IReadOnlyCollection<TrayIcon> Icons { get; }

    event Action<TrayIcon>? Added;

    event Action<TrayIcon>? Changed;

    event Action<TrayIcon>? Removed;

    /// <summary>The app asked for a balloon notification: its title, its text, and its <c>NIIF_</c> flags.</summary>
    event Action<TrayIcon, string, string, uint>? Balloon;

    void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at);
}
