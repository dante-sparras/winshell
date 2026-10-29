using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A bottom anchor combined with a vertical center sizes the item the way a right anchor and a horizontal center do.</summary>
public class AnchorVerticalCenterTests
{
    [Fact]
    public void Bottom_and_vertical_center_set_the_height()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              width: 200; height: 100
              Rectangle {
                objectName: "box"
                anchors.bottom: parent.bottom
                anchors.bottomMargin: 10
                anchors.verticalCenter: parent.verticalCenter
              }
            }
            """));
        using var _ = shell;
        var root = (Item)shell.Load();
        var box = (Item)root.ChildItems.Single(child => child.ObjectName == "box");
        Assert.Equal((10.0, 80.0), (box.Y, box.Height));
    }
}
