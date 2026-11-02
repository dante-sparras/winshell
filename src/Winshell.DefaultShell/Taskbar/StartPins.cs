namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// <c>launcher.toml</c>, the apps pinned to Start, beside the shell. An empty array pins nothing. A missing file is
/// not a choice: Start writes its defaults. Winshell Settings can reorder these pins; that window is not ported,
/// and a change in the file still shows up here.
/// </summary>
public sealed class StartPins(string path, IEventLoop loop, Log? log = null) : TomlFile(path, loop, log)
{
    /// <summary>App ids, in order. Null until the file has a pins array. <c>[]</c> pins nothing.</summary>
    [TomlComment("The apps pinned to Start, in order. [] pins nothing; without this file Start pins its defaults.")]
    public IReadOnlyList<object?>? Pins { get; set => Set(ref field, value); }
}
