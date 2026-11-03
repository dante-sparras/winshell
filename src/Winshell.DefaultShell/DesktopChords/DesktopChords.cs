using Winshell.Input;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// Alt+Tab, Alt+Shift+Tab, Win+Tab, Win+D and Win+X. Built only while <see cref="Preferences.DesktopChords"/> is on,
/// so a resting shell registers none of them. One hotkey each; disposing the registration gives the keys back.
/// </summary>
internal static class DesktopChords
{
    public static IReadOnlyList<string> Sequences { get; } = ["Alt+Tab", "Alt+Shift+Tab", "Win+Tab", "Win+D", "Win+X"];

    public static ChordRegistration Register(
        Func<Hotkey> create,
        DesktopSwitcher switcher,
        Func<IReadOnlyList<IOpenWindow>> windows,
        Func<IOpenWindow?> active,
        Func<bool> hasTaskbar)
    {
        return new ChordRegistration(create,
        [
            ("Alt+Tab", () => switcher.Cycle(1, windows(), active())),
            ("Alt+Shift+Tab", () => switcher.Cycle(-1, windows(), active())),
            ("Win+Tab", () => switcher.ToggleTaskView(windows(), active())),
            ("Win+D", () => switcher.ShowDesktop(windows())),
            ("Win+X", () => switcher.TogglePower(hasTaskbar())),
        ]);
    }
}

/// <summary>
/// The desktop chords for as long as the preference is on. Turning it off gives the keys back and closes both windows.
/// A second time on does not register the chords again.
/// </summary>
internal sealed class DesktopChordSession : IDisposable
{
    private ChordRegistration? _registration;

    public DesktopSwitcher Switcher { get; } = new();

    public void Sync(
        bool enabled,
        Func<Hotkey> create,
        Func<IReadOnlyList<IOpenWindow>> windows,
        Func<IOpenWindow?> active,
        Func<bool> hasTaskbar)
    {
        if (!enabled)
        {
            Release();
            return;
        }
        _registration ??= DesktopChords.Register(create, Switcher, windows, active, hasTaskbar);
    }

    public void Dispose() => Release();

    private void Release()
    {
        Switcher.CloseAll();
        _registration?.Dispose();
        _registration = null;
    }
}
