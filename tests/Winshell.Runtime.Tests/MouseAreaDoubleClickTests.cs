using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class MouseAreaDoubleClickTests
{
    [Fact]
    public void A_double_click_does_not_emit_clicked_again()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              property int clicks: 0
              property int doubles: 0
              property bool pressedOnDouble: false
              MouseArea {
                x: 10; y: 10; width: 40; height: 40
                onClicked: clicks++
                onDoubleClicked: { doubles++; pressedOnDouble = pressed }
              }
            }
            """);
        using var _ = shell;
        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        scene.PointerPress(20, 20, QtEnums.LeftButton, 0, true);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(1, root.ReadProperty("clicks"));
        Assert.Equal(1, root.ReadProperty("doubles"));
        Assert.True((bool)root.ReadProperty("pressedOnDouble")!);
    }

    [Fact]
    public void A_double_click_without_a_handler_still_clicks()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              property int clicks: 0
              MouseArea { x: 10; y: 10; width: 40; height: 40; onClicked: clicks++ }
            }
            """);
        using var _ = shell;
        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        scene.PointerPress(20, 20, QtEnums.LeftButton, 0, true);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(2, root.ReadProperty("clicks"));
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
        return (root, scene, shell);
    }
}
