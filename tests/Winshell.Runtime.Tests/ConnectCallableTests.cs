namespace Winshell.Runtime.Tests;

/// <summary>connect rejects a value that is not a function, and disconnect(object, function) removes only that pair.</summary>
public class ConnectCallableTests
{
    [Fact]
    public void Connect_rejects_a_value_that_is_not_a_function()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              signal ping()
              property int hits: 0
              property int caught: 0
              property QtObject left: QtObject {}
              function mark() { hits++ }
              Component.onCompleted: {
                try { ping.connect("no") } catch (e) { caught++ }
                try { ping.connect(left, "no") } catch (e) { caught++ }
                ping.connect(mark)
              }
              function fire() { ping() }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "fire()");

        Assert.Equal(2, root.ReadProperty("caught"));
        Assert.Equal(1, root.ReadProperty("hits"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Disconnect_with_an_object_removes_only_that_connection()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              signal ping()
              property int hits: 0
              property QtObject left: QtObject {}
              property QtObject right: QtObject {}
              function mark() { hits++ }
              Component.onCompleted: {
                ping.connect(mark)
                ping.connect(left, mark)
                ping.connect(right, mark)
              }
              function cutLeft() { ping.disconnect(left, mark) }
              function fire() { ping() }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "fire()");
        Assert.Equal(3, root.ReadProperty("hits"));

        shell.Eval(root, "cutLeft()");
        shell.Eval(root, "fire()");

        Assert.Equal(5, root.ReadProperty("hits"));
        Assert.Empty(shell.Messages);
    }
}
