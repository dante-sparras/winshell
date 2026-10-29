using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>The first script error of a load is reported the same way a failed load is.</summary>
public class ScriptErrorNoticeTests
{
    [Fact]
    public void A_binding_error_is_reported_and_the_shell_stays_up()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              objectName: "up"
              property string t: missing.text
            }
            """));
        using var host = new ShellHost(Path.Combine(shell.Directory, "shell.qml"), shell.Loop, null, shell.Log, StandardModules.Create)
        {
            WatchFiles = false,
        };
        string? reported = "missing";
        host.Reloaded += error => reported = error;

        Assert.True(host.Start());

        Assert.Equal("up", host.Root?.ObjectName);
        Assert.Contains("ReferenceError", reported, StringComparison.Ordinal);
        Assert.Contains(shell.Messages, message => message.Contains("ReferenceError", StringComparison.Ordinal));
    }
}
