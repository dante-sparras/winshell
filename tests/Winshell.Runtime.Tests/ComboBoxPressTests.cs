using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ComboBoxPressTests
{
    [Fact]
    public void A_click_after_an_outside_press_still_opens_the_list()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Controls
            Item {
              width: 300; height: 300
              ComboBox { id: box; objectName: "box"; x: 10; y: 40; width: 140; height: 36; model: ["First", "Second"] }
              MouseArea { x: 10; y: 240; width: 80; height: 40 }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(300);
        scene.Root.SetLayoutHeight(300);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        using var _ = shell;

        Click(scene, 40, 55);
        shell.Loop.Process();
        Assert.True(Open(root));

        Click(scene, 40, 260);
        shell.Loop.Process();
        Assert.False(Open(root));

        Click(scene, 40, 55);
        shell.Loop.Process();
        Assert.True(Open(root));
    }

    private static void Click(Scene scene, double x, double y)
    {
        scene.PointerPress(x, y, QtEnums.LeftButton);
        scene.PointerRelease(x, y, QtEnums.LeftButton);
    }

    private static bool Open(Item root)
    {
        var box = root.ChildItems.Single(i => i.ObjectName == "box");
        var popup = (QObject)box.ReadProperty("popup")!;
        return (bool)popup.ReadProperty("visible")!;
    }
}
