using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A handler's point reports scene positions in the window, and a WheelHandler takes only its own axis.</summary>
[Collection(ThemeCollection.Name)]
public class PointerHandlerEventTests
{
    [Fact]
    public void Scene_press_position_is_in_window_coordinates()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              width: 200
              height: 200
              property point local
              property point scene
              Item {
                x: 30; y: 40; width: 100; height: 100
                Item {
                  x: 10; y: 5; width: 50; height: 50
                  TapHandler {
                    id: tap
                    onPressedChanged: if (pressed) { root.local = tap.point.pressPosition; root.scene = tap.point.scenePressPosition }
                  }
                }
              }
            }
            """));
        var (root, scene) = Show(shell);

        scene.PointerPress(50, 60, QtEnums.LeftButton);

        Assert.Equal(new QPoint(10, 15), root.ReadProperty("local"));
        Assert.Equal(new QPoint(50, 60), root.ReadProperty("scene"));
    }

    [Fact]
    public void A_vertical_wheel_handler_leaves_a_horizontal_wheel_to_the_flickable_underneath()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              width: 100
              height: 100
              property real value: 0
              property int wheels: 0
              property alias contentX: flick.contentX
              Flickable {
                id: flick
                anchors.fill: parent
                contentWidth: 1000
                contentHeight: 100
              }
              Item {
                anchors.fill: parent
                WheelHandler {
                  property: "value"
                  target: root
                  onWheel: root.wheels++
                }
              }
            }
            """));
        var (root, scene) = Show(shell);

        scene.Wheel(50, 50, -120, 0);
        shell.Loop.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(0.0, Convert.ToDouble(root.ReadProperty("value")));
        Assert.Equal(0, Convert.ToInt32(root.ReadProperty("wheels")));
        Assert.True(Convert.ToDouble(root.ReadProperty("contentX")) > 0);
    }

    [Fact]
    public void A_horizontal_wheel_handler_uses_the_horizontal_angle()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              width: 100
              height: 100
              property real value: 0
              WheelHandler {
                orientation: Qt.Horizontal
                property: "value"
              }
            }
            """));
        var (root, scene) = Show(shell);

        scene.Wheel(50, 50, 0, 120);
        Assert.Equal(0.0, Convert.ToDouble(root.ReadProperty("value")));

        scene.Wheel(50, 50, 120, 0);
        Assert.Equal(15.0, Convert.ToDouble(root.ReadProperty("value")));
    }

    [Fact]
    public void A_wheel_handler_moves_its_property_by_the_pixel_delta_when_there_is_one()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              width: 100
              height: 100
              property real value: 0
              WheelHandler { property: "value" }
            }
            """));
        var (root, scene) = Show(shell);

        scene.Wheel(50, 50, 0, 120, pixelY: 7);

        Assert.Equal(7.0, Convert.ToDouble(root.ReadProperty("value")));
    }

    private static (Item Root, Scene Scene) Show(TestShell shell)
    {
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(root.Width);
        scene.Root.SetLayoutHeight(root.Height);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene);
    }
}
