using Jint.Native;

namespace Winshell.Runtime.Tests;

/// <summary>A scope write skips a destroyed object, and it updates a context property the way a read finds it.</summary>
public class ScopeSetTests
{
    [Fact]
    public void A_write_after_destruction_does_not_change_the_object()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int n: 1
            }
            """));
        var root = shell.Load();
        var scope = new ScopeObject(shell.Engine.Js, root, root.CreationContext!);
        var set = shell.Engine.Js.Compile("n = 4", [], scope, "test");
        shell.Engine.Js.Engine.Call(set, JsValue.Undefined, []);
        Assert.Equal(4, root.ReadProperty("n"));

        root.Destroy();
        var again = shell.Engine.Js.Compile("n = 9", [], scope, "test-destroyed");
        shell.Engine.Js.Engine.Call(again, JsValue.Undefined, []);

        Assert.Equal(4, root.ReadProperty("n"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void A_write_updates_a_context_property()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property string name: "object"
            }
            """));
        var root = shell.Load();
        root.CreationContext!.SetContextProperty("label", "old");
        var scope = new ScopeObject(shell.Engine.Js, root, root.CreationContext);
        var set = shell.Engine.Js.Compile("label = 'new'", [], scope, "test");
        shell.Engine.Js.Engine.Call(set, JsValue.Undefined, []);

        Assert.True(root.CreationContext.TryGetContextProperty("label", out var label));
        Assert.Equal("new", label);
        Assert.Equal("object", root.ReadProperty("name"));
        Assert.Empty(shell.Messages);
    }
}
