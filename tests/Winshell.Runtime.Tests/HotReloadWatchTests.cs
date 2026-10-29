using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Replacing the shell folder must not leave hot reload dead.</summary>
public class HotReloadWatchTests
{
    [Fact]
    public void Replacing_the_shell_folder_reloads_when_the_folder_returns()
    {
        const string before = """
            import Winshell
            ShellRoot { property int marker: 1 }
            """;
        const string after = """
            import Winshell
            ShellRoot { property int marker: 2 }
            """;
        using var shell = new TestShell(("shell.qml", before));
        using var host = new ShellHost(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log);
        var reloads = 0;
        host.Reloaded += _ => reloads++;
        Assert.True(host.Start());
        Directory.Delete(shell.Directory, true);
        // Let the dead watcher notice the folder is gone before the folder comes back.
        Pump(shell, TimeSpan.FromSeconds(1));
        Directory.CreateDirectory(shell.Directory);
        File.WriteAllText(Path.Combine(shell.Directory, "shell.qml"), after);
        Pump(shell, TimeSpan.FromSeconds(4));

        Assert.Contains(shell.Messages, line => line.Contains("Configuration Loaded", StringComparison.Ordinal));
        Assert.True(reloads > 1);
    }

    private static void Pump(TestShell shell, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            shell.Loop.Process();
            shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
            Thread.Sleep(30);
        }
    }
}
