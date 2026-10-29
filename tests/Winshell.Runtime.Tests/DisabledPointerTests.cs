using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A disabled item and its children do not take taps, and the press falls through.</summary>
public class DisabledPointerTests
{
    [Fact]
    public void A_disabled_item_does_not_take_a_press()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              width: 100
              height: 100
              property int taps: 0
              property int under: 0
              MouseArea {
                x: 10; y: 10; width: 40; height: 40
                onClicked: { under++ }
              }
              Item {
                x: 10; y: 10; width: 40; height: 40
                enabled: false
                TapHandler { onTapped: { taps++ } }
                Item {
                  width: 40; height: 40
                  DragHandler { target: root }
                  TapHandler { onTapped: { taps++ } }
                }
              }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(100);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();

        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerMove(30, 24);
        scene.PointerRelease(30, 24, QtEnums.LeftButton);

        Assert.Equal(0, Convert.ToInt32(root.ReadProperty("taps")));
        Assert.Equal(1, Convert.ToInt32(root.ReadProperty("under")));
        Assert.Equal(0, root.X);
        Assert.Equal(0, root.Y);
    }
}
