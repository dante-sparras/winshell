using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Replacing the shell folder must not leave hot reload dead.</summary>
public class HotReloadWatchTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(30);

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
        Reloaded(shell, host);

        Assert.Contains(shell.Messages, line => line.Contains("Configuration Loaded", StringComparison.Ordinal));
        Assert.True(reloads > 1);
        Assert.Equal(2, host.Root!.ReadProperty("marker"));
    }

    [Fact]
    public void A_nested_shell_folder_can_return_one_parent_at_a_time()
    {
        using var shell = new TestShell(("config/inside/shell.qml", "import Winshell; ShellRoot { property int marker: 1 }"));
        var config = Path.Combine(shell.Directory, "config");
        var inside = Path.Combine(config, "inside");
        using var host = new ShellHost(Path.Combine(inside, "shell.qml"), shell.Loop, null, shell.Log);
        Assert.True(host.Start());
        Directory.Delete(config, true);
        Pump(shell, TimeSpan.FromSeconds(1));
        Directory.CreateDirectory(config);
        Pump(shell, TimeSpan.FromSeconds(1));
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "shell.qml"), "import Winshell; ShellRoot { property int marker: 2 }");
        Reloaded(shell, host);

        Assert.Equal(2, host.Root!.ReadProperty("marker"));
    }

    /// <summary>Runs the loop for a while. A watch must recover whether it saw the folder go before it came back or not.</summary>
    private static void Pump(TestShell shell, TimeSpan time)
    {
        for (var pumped = TimeSpan.Zero; pumped < time; pumped += Step)
        {
            Wait.ForNothingToHappen(Step);
            RunLoop(shell);
        }
    }

    private static void Reloaded(TestShell shell, ShellHost host) =>
        Wait.Until(() => Equals(host.Root?.ReadProperty("marker"), 2), "the reload", () => RunLoop(shell));

    private static void RunLoop(TestShell shell)
    {
        shell.Loop.Process();
        shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
    }
}
