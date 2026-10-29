using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Hover hit-tests a rotated item where it is drawn, as a press does.</summary>
public class HoverRotationTests
{
    [Fact]
    public void A_rotated_mouse_area_is_hovered_inside_its_rotated_shape_only()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              width: 100; height: 100
              property bool hovered: area.containsMouse
              // 80 wide and 20 tall, turned upright around its centre (50, 50): it covers x 40-60, y 10-90.
              MouseArea { id: area; x: 10; y: 40; width: 80; height: 20; rotation: 90; hoverEnabled: true }
            }
            """));
        using var _ = shell;
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(100);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();

        scene.PointerMove(50, 15);
        Assert.True((bool)root.ReadProperty("hovered")!);

        scene.PointerMove(15, 50);
        Assert.False((bool)root.ReadProperty("hovered")!);
    }
}
