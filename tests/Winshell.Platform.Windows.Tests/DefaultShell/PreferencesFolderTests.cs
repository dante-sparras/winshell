using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>Where <c>preferences.toml</c> lives, so two configs running at once do not share one file.</summary>
[Collection(ThemeCollection.Name)]
public class PreferencesFolderTests
{
    private const string View = """
        import Winshell
        import QtQuick
        Item {
          property string path: Preferences.path
          property string folder: Preferences.folder
        }
        """;

    [Fact]
    public void A_config_in_the_config_folder_keeps_its_own_file()
    {
        // A named config: <root>/<name>/shell.qml.
        using var shell = new DefaultShellHarness(View, configure: e => e.AddService<IShellHost>(new Host(e.ShellDir!, Path.GetDirectoryName(e.ShellDir)!)));
        var shellDir = shell.Directory;

        Assert.Equal(Path.GetFullPath(shellDir), Path.GetFullPath((string)shell.Root.ReadProperty("folder")!));
        Assert.Equal(Path.Combine(shellDir, "preferences.toml"), Path.GetFullPath((string)shell.Root.ReadProperty("path")!));
    }

    [Fact]
    public void The_config_folder_itself_keeps_the_file_there()
    {
        using var shell = new DefaultShellHarness(View, configure: e => e.AddService<IShellHost>(new Host(e.ShellDir!, e.ShellDir!.ToUpperInvariant() + "\\")));

        Assert.Equal(Path.Combine(shell.Directory, "preferences.toml"), Path.GetFullPath((string)shell.Root.ReadProperty("path")!));
    }

    [Fact]
    public void A_shell_outside_the_config_folder_uses_the_config_folder()
    {
        // The default shell runs from %LOCALAPPDATA%\winshell\default-shell, outside %APPDATA%\winshell.
        var config = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        using var shell = new DefaultShellHarness(View, configure: e => e.AddService<IShellHost>(new Host(e.ShellDir!, config)));

        Assert.Equal(config, Path.GetFullPath((string)shell.Root.ReadProperty("folder")!));
        Assert.Equal(Path.Combine(config, "preferences.toml"), Path.GetFullPath((string)shell.Root.ReadProperty("path")!));
    }

    private sealed class Host(string shellDir, string configRoot) : IShellHost
    {
        public string ShellId => "preferences-folder-test";
        public string InstanceId => "test";
        public string AppId => "winshell";
        public DateTime LaunchTime { get; } = DateTime.Now;
        public string ShellDir => shellDir;
        public string DataDir => Path.Combine(shellDir, "data");
        public string StateDir => Path.Combine(shellDir, "state");
        public string CacheDir => Path.Combine(shellDir, "cache");
        public string ConfigRoot => configRoot;
        public bool WatchFiles { get; set; }
        public void Reload(bool hard) { }
        public void InhibitReloadPopup() { }
    }
}
