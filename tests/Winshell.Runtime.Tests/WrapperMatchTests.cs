namespace Winshell.Runtime.Tests;

/// <summary>A script assignment does not deep-copy a value just to see its type, and a cycle can be logged.</summary>
public class WrapperMatchTests
{
    [Fact]
    public void Console_log_of_a_cycle_does_not_throw()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              Component.onCompleted: {
                var a = { name: "a" };
                a.self = a;
                console.log(a);
              }
            }
            """));
        shell.Load();

        Assert.DoesNotContain(shell.Messages, message => message.Contains("Cyclic", StringComparison.Ordinal));
        Assert.Contains(shell.Messages, message => message.StartsWith("Info ", StringComparison.Ordinal));
    }

    [Fact]
    public void Assigning_Qt_binding_still_makes_a_binding()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int n: 1
              property int hits: 0
              Component.onCompleted: n = Qt.binding(function() { hits++; return 4 })
            }
            """));
        var root = shell.Load();

        Assert.Equal(4, root.ReadProperty("n"));
        Assert.Equal(1, root.ReadProperty("hits"));
        Assert.Empty(shell.Messages);
    }
}
