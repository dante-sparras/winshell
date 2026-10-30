namespace Winshell.Runtime.Tests;

/// <summary>A wrapper must not keep a disposed runtime alive, and a destroyed object must not keep one either.</summary>
public class ReleaseWrapperTests
{
    [Fact]
    public void Disposing_the_engine_drops_a_wrapper_it_does_not_own()
    {
        var outside = new QObject();
        var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject { }
            """));
        shell.Load();
        var wrapper = shell.Engine.Js.Wrap(outside);

        Assert.Same(wrapper, outside.Wrapper);
        shell.Dispose();
        Assert.Null(outside.Wrapper);
    }

    [Fact]
    public void Destroying_an_object_drops_its_wrapper()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject { objectName: "root" }
            """));
        var root = shell.Load();
        shell.Engine.Js.Wrap(root);

        Assert.NotNull(root.Wrapper);
        root.Destroy();
        Assert.Null(root.Wrapper);
        Assert.True(root.IsDestroyed);
        Assert.Empty(shell.Messages);
    }
}
