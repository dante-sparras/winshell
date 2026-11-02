using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>The default shell: a taskbar along the bottom of the screen.</summary>
public sealed class DefaultShellConfiguration : ShellConfiguration
{
    private Preferences _preferences = null!;
    private TaskbarPins _pins = null!;
    private Taskbars _bars = null!;

    public override void Start(Shell shell)
    {
        _preferences = new Preferences(Path.Combine(shell.ConfigRoot, "preferences.toml"), shell.Loop, shell.Log);
        _pins = new TaskbarPins(shell.DataPath("taskbar.json"), shell.Loop, shell.Log);
        _bars = new Taskbars(shell, _preferences, _pins);
    }
}
