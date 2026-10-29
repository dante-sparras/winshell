using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Hiding or disabling the focused item, or an ancestor, takes keyboard focus away.</summary>
public class FocusHideTests
{
    [Fact]
    public void Hiding_or_disabling_the_focused_item_clears_focus()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              id: box
              property Item field: input
              TextInput { id: input; text: "hi"; focus: true }
            }
            """);
        using var _ = shell;
        var field = (Item)root.ReadProperty("field")!;
        Assert.Same(field, scene.FocusItem);
        Assert.True(field.ActiveFocus);

        root.Visible = false;
        Assert.Null(scene.FocusItem);
        Assert.False(field.ActiveFocus);

        root.Visible = true;
        field.ForceActiveFocus();
        Assert.Same(field, scene.FocusItem);
        root.Enabled = false;
        Assert.Null(scene.FocusItem);
        Assert.False(field.ActiveFocus);

        root.Enabled = true;
        field.ForceActiveFocus();
        field.Focus = false;
        Assert.Null(scene.FocusItem);
        Assert.False(field.ActiveFocus);
    }

    [Fact]
    public void A_child_hears_when_its_parent_is_disabled()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              id: box
              property Item child: inner
              Item {
                id: inner
                property int notes: 0
                onEnabledChanged: notes = notes + 1
              }
            }
            """);
        using var _ = shell;
        var child = (Item)root.ReadProperty("child")!;
        var before = Convert.ToInt32(child.ReadProperty("notes"));
        root.Enabled = false;
        Assert.False(child.Enabled);
        Assert.True(Convert.ToInt32(child.ReadProperty("notes")) > before);
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(200);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }
}
