using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A MouseArea builds a <c>mouse</c> event for a move only when something handles positionChanged.</summary>
public class MouseAreaMoveEventTests
{
    [Fact]
    public void A_move_without_a_handler_makes_no_event()
    {
        var (shell, scene, _) = Load("""
            import QtQuick
            Item {
              width: 100
              height: 100
              MouseArea { x: 10; y: 10; width: 60; height: 60; hoverEnabled: true }
            }
            """);
        using var _ = shell;

        var before = MouseEvent.Created;
        scene.PointerMove(20, 20);
        scene.PointerMove(30, 30);
        Assert.Equal(before, MouseEvent.Created);

        scene.PointerPress(30, 30, QtEnums.LeftButton);
        before = MouseEvent.Created;
        scene.PointerMove(40, 35);
        Assert.Equal(before, MouseEvent.Created);
        scene.PointerRelease(40, 35, QtEnums.LeftButton);
    }

    [Fact]
    public void A_move_with_a_handler_reports_where_the_pointer_is()
    {
        var (shell, scene, root) = Load("""
            import QtQuick
            Item {
              width: 100
              height: 100
              property real seenX: -1
              property real seenY: -1
              MouseArea {
                x: 10; y: 10; width: 60; height: 60
                hoverEnabled: true
                onPositionChanged: (mouse) => { seenX = mouse.x; seenY = mouse.y }
              }
            }
            """);
        using var _ = shell;

        scene.PointerMove(30, 24);
        Assert.Equal(20, Convert.ToDouble(root.ReadProperty("seenX")));
        Assert.Equal(14, Convert.ToDouble(root.ReadProperty("seenY")));

        scene.PointerPress(30, 24, QtEnums.LeftButton);
        scene.PointerMove(45, 50);
        Assert.Equal(35, Convert.ToDouble(root.ReadProperty("seenX")));
        Assert.Equal(40, Convert.ToDouble(root.ReadProperty("seenY")));
        scene.PointerRelease(45, 50, QtEnums.LeftButton);
    }

    private static (TestShell Shell, Scene Scene, Item Root) Load(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(100);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (shell, scene, root);
    }
}
