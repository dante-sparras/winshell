namespace Winshell.Runtime.Tests;

/// <summary>console.trace includes a stack, and a throwing toString does not make console.log throw.</summary>
public class ConsoleTests
{
    [Fact]
    public void Trace_logs_a_stack_and_a_throwing_toString_is_logged()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property var bad: ({ toString: function() { throw new Error("boom") } })
              Component.onCompleted: {
                console.log(bad)
                console.trace("where")
              }
            }
            """));
        shell.Load();

        Assert.Contains(shell.Messages, m => m.Contains("[object Object]", StringComparison.Ordinal));
        var trace = Assert.Single(shell.Messages, m => m.Contains("where", StringComparison.Ordinal));
        Assert.Contains("trace", trace, StringComparison.Ordinal);
        Assert.Contains(shell.Directory, trace, StringComparison.Ordinal);
    }
}
