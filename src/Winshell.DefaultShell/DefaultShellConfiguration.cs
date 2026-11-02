using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The default shell: a taskbar along the bottom of the screen.</summary>
public sealed class DefaultShellConfiguration : ShellConfiguration
{
    private Taskbars? _bars;

    public override void Start(Shell shell)
    {
        var preferences = new Preferences(Path.Combine(shell.ConfigRoot, "preferences.toml"), shell.Loop, shell.Log);
        var pins = new TaskbarPins(shell.DataPath("taskbar.json"), shell.Loop, shell.Log);
        _bars = new Taskbars(shell, preferences, pins);
    }
}
