namespace Winshell.Runtime.Tests;

/// <summary>Tearing an object down must not re-run its bindings against the half-destroyed tree.</summary>
public class DestroyBindingTests
{
    [Fact]
    public void Destroying_an_item_does_not_re_evaluate_its_bindings()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              objectName: "root"
              property real w: children[0].implicitWidth
              Item { implicitWidth: 12 }
            }
            """));
        var root = shell.Load();
        Assert.Equal(12d, root.ReadProperty("w"));

        root.Destroy();

        Assert.True(root.IsDestroyed);
        Assert.True(root.IsDestroying);
        Assert.DoesNotContain(shell.Messages, m => m.Contains("TypeError", StringComparison.Ordinal) || m.StartsWith("Warn", StringComparison.Ordinal) || m.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public void A_living_parent_still_sees_a_child_leave()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              property int n: children.length
              Item { id: gone; objectName: "gone" }
              Item { objectName: "stay" }
              function drop() { gone.destroy() }
            }
            """));
        var root = shell.Load();
        Assert.Equal(2, root.ReadProperty("n"));

        shell.Eval(root, "drop()");
        shell.Loop.Process();

        Assert.Equal(1, root.ReadProperty("n"));
        Assert.False(root.IsDestroying);
        Assert.Empty(shell.Messages);
    }
}
