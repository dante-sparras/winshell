namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// <c>quick-settings.toml</c>, the tiles in order. Quick settings watches this file, and assigning <see cref="Pinned"/>
/// writes it. A key the file does not have keeps the default order.
/// </summary>
public sealed class QuickSettingsLayout(string path, IEventLoop loop, Log? log = null) : TomlFile(path, loop, log)
{
    [TomlComment("The quick settings tiles, in order: wifi, bluetooth, airplaneMode, energySaver, powerMode, nightLight,\naccessibility. A tile left out is not shown until quick settings or Winshell Settings adds it back.")]
    public IReadOnlyList<object?>? Pinned { get; set => Set(ref field, value); }
}
