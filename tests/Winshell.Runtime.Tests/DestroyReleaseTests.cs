namespace Winshell.Runtime.Tests;

/// <summary>A destroyed object lets go of its functions and context, so a stale reference does not keep them alive.</summary>
public class DestroyReleaseTests
{
    [Fact]
    public void A_destroyed_object_drops_its_functions_and_context()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property QtObject stale
              property Component maker: Component {
                QtObject {
                  function twice(x) { return x * 2 }
                }
              }
              Component.onCompleted: stale = maker.createObject(null)
            }
            """));
        var root = shell.Load();
        var child = (QObject)root.ReadProperty("stale")!;
        Assert.NotNull(child.CreationContext);

        child.Destroy();

        Assert.Null(child.CreationContext);
        Assert.Null(child.GetFunction("twice"));
        Assert.Null(child.FunctionDeclaration("twice"));
        Assert.Empty(child.FunctionNames);
        Assert.Equal(true, shell.Eval(root, "stale.twice === undefined"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Destruction_handlers_still_see_the_object_functions()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              id: root
              property int seen: 0
              property QtObject child: QtObject {
                property int n: 3
                function read() { return n }
                Component.onDestruction: root.seen = read()
              }
            }
            """));
        var root = shell.Load();
        var child = (QObject)root.ReadProperty("child")!;

        child.Destroy();

        Assert.Equal(3, root.ReadProperty("seen"));
        Assert.Empty(shell.Messages);
    }
}
