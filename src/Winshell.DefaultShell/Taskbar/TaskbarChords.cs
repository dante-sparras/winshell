using Winshell.Input;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>What the main taskbar's chords ask for. Win+Alt+number is not here: Windows keeps it.</summary>
internal interface ITaskbarChordActions
{
    void FocusApps();

    void ToggleQuickSettings();

    void ToggleNotifications();

    void OpenSearch();

    /// <summary>The app at <paramref name="index"/>, counted from the first button. A new window is Win+Shift+number.</summary>
    void InvokeApp(int index, bool newWindow);
}

/// <summary>
/// Win+T, Win+A, Win+N, Win+S, and Win+1 through Win+9 and Win+0, with Shift for a new window. One hotkey each.
/// A chord Windows already holds is <see cref="Hotkey.Taken"/> and is not thrown: the hotkey reports it.
/// </summary>
internal static class TaskbarChords
{
    private static readonly (string Sequence, Action<ITaskbarChordActions> Act)[] Map = Build();

    public static IReadOnlyList<string> Sequences { get; } = Map.Select(item => item.Sequence).ToArray();

    public static ChordRegistration Register(Func<Hotkey> create, ITaskbarChordActions actions) =>
        new(create, Map.Select(item => (item.Sequence, (Action)(() => item.Act(actions)))).ToArray());

    private static (string Sequence, Action<ITaskbarChordActions> Act)[] Build()
    {
        var list = new List<(string, Action<ITaskbarChordActions>)>
        {
            ("Win+T", static actions => actions.FocusApps()),
            ("Win+A", static actions => actions.ToggleQuickSettings()),
            ("Win+N", static actions => actions.ToggleNotifications()),
            ("Win+S", static actions => actions.OpenSearch()),
        };
        foreach (var (key, index) in Numbers())
        {
            var place = index;
            list.Add(("Win+" + key, actions => actions.InvokeApp(place, false)));
        }
        foreach (var (key, index) in Numbers())
        {
            var place = index;
            list.Add(("Win+Shift+" + key, actions => actions.InvokeApp(place, true)));
        }
        return [.. list];
    }

    /// <summary>Win+1 is the first app and Win+0 is the tenth, as the number row is drawn.</summary>
    private static IEnumerable<(string Key, int Index)> Numbers()
    {
        for (var number = 1; number <= 9; number++)
            yield return (number.ToString(System.Globalization.CultureInfo.InvariantCulture), number - 1);
        yield return ("0", 9);
    }
}

/// <summary>One <see cref="Hotkey"/> per sequence. Disposing it gives the keys back.</summary>
internal sealed class ChordRegistration : IDisposable
{
    private readonly List<Hotkey> _hotkeys = [];

    public ChordRegistration(Func<Hotkey> create, IReadOnlyList<(string Sequence, Action Press)> chords)
    {
        foreach (var (sequence, press) in chords)
        {
            var hotkey = create();
            hotkey.Pressed += (_, _) => press();
            hotkey.Sequence = sequence;
            _hotkeys.Add(hotkey);
        }
    }

    public void Dispose()
    {
        foreach (var hotkey in _hotkeys)
            hotkey.Dispose();
        _hotkeys.Clear();
    }
}
