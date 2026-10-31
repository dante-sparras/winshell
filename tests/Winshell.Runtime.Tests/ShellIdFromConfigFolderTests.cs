using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A config in the config folder keeps its id, and so its data and state, when that folder moves.</summary>
public class ShellIdFromConfigFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "winshell-id-root");

    [Fact]
    public void The_config_folder_itself_is_root()
    {
        Assert.Equal("root", ShellHost.DefaultShellId(Path.Combine(Root, "shell.qml"), Root));
        Assert.Equal("root", ShellHost.DefaultShellId(Path.Combine(Root, "shell.qml"), Root.ToUpperInvariant() + "\\"));
    }

    [Fact]
    public void A_named_config_is_its_folder_name()
    {
        Assert.Equal("work", ShellHost.DefaultShellId(Path.Combine(Root, "Work", "shell.qml"), Root));
    }

    [Fact]
    public void A_moved_config_folder_keeps_its_ids()
    {
        var moved = Path.Combine(Path.GetTempPath(), "winshell-id-moved");
        Assert.Equal(
            ShellHost.DefaultShellId(Path.Combine(Root, "work", "shell.qml"), Root),
            ShellHost.DefaultShellId(Path.Combine(moved, "work", "shell.qml"), moved));
        Assert.Equal(
            ShellHost.DefaultShellId(Path.Combine(Root, "shell.qml"), Root),
            ShellHost.DefaultShellId(Path.Combine(moved, "shell.qml"), moved));
    }

    [Fact]
    public void A_shell_anywhere_else_is_a_hash_of_its_path()
    {
        var outside = Path.Combine(Path.GetTempPath(), "winshell-id-elsewhere", "shell.qml");
        var deeper = Path.Combine(Root, "a", "b", "shell.qml");

        Assert.Matches("^[0-9a-f]{32}$", ShellHost.DefaultShellId(outside, Root));
        Assert.Matches("^[0-9a-f]{32}$", ShellHost.DefaultShellId(deeper, Root));
        Assert.Matches("^[0-9a-f]{32}$", ShellHost.DefaultShellId(Path.Combine(Root, "shell.qml"), ""));
    }

    [Fact]
    public void The_host_names_its_folders_after_the_config_folder()
    {
        using var files = new TestShell(("shell.qml", "import Winshell\nShellRoot { }\n"));
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log) { ConfigRoot = files.Directory };

        Assert.Equal("root", host.ShellId);
        Assert.Equal(Path.Combine(ShellLocations.DataRoot, "by-shell", "root"), host.DataDir);
        Assert.Equal(Path.Combine(ShellLocations.StateRoot, "by-shell", "root"), host.StateDir);
        Assert.Equal(Path.Combine(ShellLocations.CacheRoot, "by-shell", "root"), host.CacheDir);
    }

    [Fact]
    public void A_shell_id_pragma_still_wins()
    {
        using var files = new TestShell(("shell.qml", "//@ pragma ShellId mine\nimport Winshell\nShellRoot { }\n"));
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log) { ConfigRoot = files.Directory };

        Assert.Equal("mine", host.ShellId);
        Assert.Equal(Path.Combine(ShellLocations.DataRoot, "by-shell", "mine"), host.DataDir);
    }
}
