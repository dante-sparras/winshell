using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class ShellLocationTests
{
    [Fact]
    public void Folders_follow_the_windows_user_profile()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(roaming, "winshell"), ShellLocations.ConfigRoot);
        Assert.Equal(Path.Combine(local, "winshell"), ShellLocations.LocalRoot);
        Assert.Equal(Path.Combine(local, "winshell", "cache"), ShellLocations.CacheRoot);
        Assert.Equal(Path.Combine(local, "winshell", "state"), ShellLocations.StateRoot);
        Assert.Equal(Path.Combine(local, "winshell", "data"), ShellLocations.DataRoot);
        Assert.Equal(Path.Combine(local, "winshell", "run"), ShellLocations.RunRoot);
        Assert.Equal(Path.Combine(local, "winshell", "default-shell"), ShellLocations.DefaultShellRoot);
    }

    [Fact]
    public void A_shell_keeps_cache_state_and_data_on_this_machine()
    {
        using var files = new TestShell(("shell.qml", """
            //@ pragma CacheDir $BASE/custom
            import Winshell
            ShellRoot {}
            """));
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        try
        {
            Assert.StartsWith(ShellLocations.DataRoot + Path.DirectorySeparatorChar, host.DataDir, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(ShellLocations.StateRoot + Path.DirectorySeparatorChar, host.StateDir, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(ShellLocations.CacheRoot, "custom"), host.CacheDir, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void A_shell_reads_the_config_folder_its_log_and_the_version()
    {
        using var files = new TestShell(("shell.qml", """
            import Winshell
            ShellRoot {
              property string configRoot: Shell.configRoot
              property string logPath: Shell.logPath
              property string version: Shell.version
            }
            """));
        var config = Path.Combine(files.Directory, "config");
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log) { ConfigRoot = config, LogPath = Path.Combine(files.Directory, "log.txt") };
        try
        {
            Assert.True(host.Start());
            Assert.Equal(config, host.Root!.ReadProperty("configRoot"));
            Assert.Equal(Path.Combine(files.Directory, "log.txt"), host.Root.ReadProperty("logPath"));
            Assert.Equal(ProductVersion.Current.Text, host.Root.ReadProperty("version"));
        }
        finally
        {
            host.Dispose();
        }

        // Without a host there is no config folder and no log.
        var bare = TestShell.Run("""
            import Winshell
            ShellRoot { property string configRoot: Shell.configRoot; property string logPath: Shell.logPath }
            """, out var shell);
        using var _ = shell;
        Assert.Equal("", bare.ReadProperty("configRoot"));
        Assert.Equal("", bare.ReadProperty("logPath"));
    }
}
