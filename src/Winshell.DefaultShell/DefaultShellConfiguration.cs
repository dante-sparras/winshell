using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The default shell: a taskbar along the bottom of the screen, and the on-screen display above it.</summary>
public sealed class DefaultShellConfiguration : ShellConfiguration
{
    private Taskbars? _bars;
    private OnScreenDisplayHost? _osd;

    public override void Start(Shell shell)
    {
        var preferences = new Preferences(Path.Combine(shell.ConfigRoot, "preferences.toml"), shell.Loop, shell.Log);
        ShellPreferences.Follow(shell, preferences);
        var pins = new TaskbarPins(shell.DataPath("taskbar.json"), shell.Loop, shell.Log);
        _bars = new Taskbars(shell, preferences, pins);
        _osd = new OnScreenDisplayHost(shell, preferences, TimeProvider.System);
    }
}
