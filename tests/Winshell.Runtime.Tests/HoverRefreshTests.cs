using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Hover follows the tree when the pointer stays still.</summary>
public class HoverRefreshTests
{
    [Fact]
    public void Destroying_the_item_under_the_pointer_hovers_what_is_below_it()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              width: 100; height: 100
              property bool belowHover: below.containsMouse
              MouseArea { id: below; anchors.fill: parent; hoverEnabled: true }
              MouseArea { id: above; objectName: "above"; anchors.fill: parent; hoverEnabled: true }
            }
            """);
        using var _ = shell;
        scene.PointerMove(40, 40);
        Assert.False((bool)root.ReadProperty("belowHover")!);

        Find(root, "above").Destroy();
        shell.Loop.Process();

        Assert.True((bool)root.ReadProperty("belowHover")!);
    }

    [Fact]
    public void Moving_or_hiding_an_item_clears_hover_without_a_pointer_move()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              width: 100; height: 100
              property bool hovered: area.containsMouse
              MouseArea { id: area; objectName: "area"; width: 30; height: 30; hoverEnabled: true }
            }
            """);
        using var _ = shell;
        var area = Find(root, "area");
        scene.PointerMove(10, 10);
        Assert.True((bool)root.ReadProperty("hovered")!);

        area.X = 80;
        shell.Loop.Process();
        Assert.False((bool)root.ReadProperty("hovered")!);

        area.X = 0;
        shell.Loop.Process();
        Assert.True((bool)root.ReadProperty("hovered")!);

        area.Visible = false;
        shell.Loop.Process();
        Assert.False((bool)root.ReadProperty("hovered")!);
    }

    [Fact]
    public void Releasing_after_the_grabbed_item_is_destroyed_does_not_click_what_is_below()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              width: 100; height: 100
              property int clicks: 0
              MouseArea { anchors.fill: parent; onClicked: clicks += 1 }
              MouseArea { objectName: "above"; anchors.fill: parent }
            }
            """);
        using var _ = shell;
        scene.PointerMove(40, 40);
        scene.PointerPress(40, 40, QtEnums.LeftButton);
        Find(root, "above").Destroy();
        scene.PointerRelease(40, 40, QtEnums.LeftButton);
        shell.Loop.Process();
        Assert.Equal(0, root.ReadProperty("clicks"));
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(100);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static Item Find(Item root, string objectName)
    {
        if (root.ObjectName == objectName)
            return root;
        foreach (var child in root.ChildItems)
        {
            try
            {
                return Find(child, objectName);
            }
            catch (InvalidOperationException)
            {
            }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }
}
