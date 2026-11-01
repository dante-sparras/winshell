using System.Globalization;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A Flickable takes over a drag only for a button it handles.</summary>
public class StealButtonTests
{
    [Fact]
    public void A_right_button_drag_stays_with_the_mouse_area_inside_a_flickable()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            Flickable {
              id: flick
              width: 100; height: 100
              contentHeight: 400
              property bool canceled: false
              property int clicks: 0
              MouseArea {
                width: 100; height: 400
                acceptedButtons: Qt.RightButton
                onCanceled: flick.canceled = true
                onClicked: mouse => { if (mouse.button === Qt.RightButton) flick.clicks += 1 }
              }
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

        scene.PointerPress(50, 80, QtEnums.RightButton);
        scene.PointerMove(50, 50);
        Assert.Equal(0.0, Convert.ToDouble(root.ReadProperty("contentY"), CultureInfo.InvariantCulture));

        scene.PointerRelease(50, 50, QtEnums.RightButton);
        shell.Loop.Process();
        Assert.False((bool)root.ReadProperty("canceled")!);
        Assert.Equal(1, root.ReadProperty("clicks"));
    }
}
