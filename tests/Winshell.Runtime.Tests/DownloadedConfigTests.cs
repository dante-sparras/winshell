using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// A <c>shell.qml</c> whose Zone.Identifier stream says it came from the Internet or Restricted zone is not run. The
/// load fails with a message that says how to unblock it, and the unblocked file loads.
/// </summary>
public class DownloadedConfigTests
{
    private const string Config = """
        import QtQml
        QtObject { objectName: "ran" }
        """;

    private static void Mark(TestShell shell, int zone) =>
        File.WriteAllText(Path.Combine(shell.Directory, "shell.qml") + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId={zone}\r\n");

    private static ShellHost Host(TestShell shell) =>
        new(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log) { WatchFiles = false };

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void A_downloaded_config_is_not_run_until_it_is_unblocked(int zone)
    {
        using var shell = new TestShell(("shell.qml", Config));
        Mark(shell, zone);
        using var host = Host(shell);
        string? reported = null;
        host.Reloaded += error => reported = error;

        Assert.False(host.Start());

        Assert.Null(host.Root);
        var root = Path.Combine(shell.Directory, "shell.qml");
        Assert.Equal($"{root}: shell.qml was downloaded from the internet. It runs as you. If you trust it, unblock it (right-click > Properties > Unblock, or Unblock-File) and save it to load it.", reported);
        Assert.Contains(shell.Messages, m => m.StartsWith("Error scene: Failed to load configuration: ", StringComparison.Ordinal)
            && m.Contains("was downloaded from the internet", StringComparison.Ordinal));

        // Unblock-File removes the stream.
        File.Delete(root + ":Zone.Identifier");
        host.Reload(hard: false);

        Assert.Equal("ran", host.Root?.ObjectName);
        Assert.Null(reported);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_config_from_this_pc_or_the_intranet_runs(int zone)
    {
        using var shell = new TestShell(("shell.qml", Config));
        Mark(shell, zone);
        using var host = Host(shell);

        Assert.True(host.Start());

        Assert.Equal("ran", host.Root?.ObjectName);
    }

    [Fact]
    public void A_config_without_a_zone_runs()
    {
        using var shell = new TestShell(("shell.qml", Config));
        using var host = Host(shell);

        Assert.True(host.Start());

        Assert.Equal("ran", host.Root?.ObjectName);
        Assert.False(ZoneIdentifier.IsDownloaded(Path.Combine(shell.Directory, "shell.qml")));
        Assert.False(ZoneIdentifier.IsDownloaded(Path.Combine(shell.Directory, "missing.qml")));
    }
}
