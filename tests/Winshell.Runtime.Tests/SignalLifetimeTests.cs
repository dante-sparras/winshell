namespace Winshell.Runtime.Tests;

/// <summary>Script connections follow the object that made them, and a disconnect takes effect in the current emit.</summary>
public class SignalLifetimeTests
{
    [Fact]
    public void A_connection_from_a_destroyed_object_does_not_run()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              property int hits: 0
              signal ping()
              property QtObject child: QtObject {
                Component.onCompleted: root.ping.connect(function() { root.hits++ })
              }
              function fire() { ping() }
            }
            """));
        var root = shell.Load();
        var child = (QObject)root.ReadProperty("child")!;
        shell.Eval(root, "fire()");
        Assert.Equal(1, root.ReadProperty("hits"));

        child.Destroy();
        shell.Eval(root, "fire()");

        Assert.Equal(1, root.ReadProperty("hits"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Disconnect_during_emit_skips_the_handler_that_was_removed()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              signal ping()
              property int hits: 0
              function first() { hits++; ping.disconnect(second) }
              function second() { hits += 10 }
              Component.onCompleted: {
                ping.connect(first)
                ping.connect(second)
              }
              function fire() { ping() }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "fire()");

        Assert.Equal(1, root.ReadProperty("hits"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Disconnect_undoes_a_signal_to_signal_connection()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              signal a()
              signal b()
              property int hits: 0
              Component.onCompleted: {
                b.connect(function() { hits++ })
                a.connect(b)
              }
              function fire() { a() }
              function cut() { a.disconnect(b) }
            }
            """));
        var root = shell.Load();
        shell.Eval(root, "fire()");
        Assert.Equal(1, root.ReadProperty("hits"));

        shell.Eval(root, "cut()");
        shell.Eval(root, "fire()");

        Assert.Equal(1, root.ReadProperty("hits"));
        Assert.Empty(shell.Messages);
    }
}
