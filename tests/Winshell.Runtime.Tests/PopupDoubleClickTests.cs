using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class PopupDoubleClickTests
{
    [Fact]
    public void A_double_click_outside_a_popup_that_stays_open_reaches_the_item_under_it()
    {
        var (root, scene, shell) = Show("modal: false; closePolicy: Popup.NoAutoClose");
        using var _ = shell;
        var popup = (Popup)root.ReadProperty("flyout")!;
        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        scene.PointerPress(20, 20, QtEnums.LeftButton, 0, true);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(1, root.ReadProperty("doubles"));
        Assert.True(popup.Opened);
    }

    [Fact]
    public void A_double_click_outside_a_popup_with_the_default_close_policy_closes_it_and_reaches_the_item_under_it()
    {
        var (root, scene, shell) = Show("modal: false");
        using var _ = shell;
        var popup = (Popup)root.ReadProperty("flyout")!;
        scene.PointerPress(20, 20, QtEnums.LeftButton, 0, true);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        shell.Loop.Process();
        Assert.Equal(1, root.ReadProperty("doubles"));
        Assert.False(popup.Opened);
    }

    [Fact]
    public void A_modal_popup_blocks_a_double_click_outside_it()
    {
        var (root, scene, shell) = Show("modal: true; closePolicy: Popup.NoAutoClose");
        using var _ = shell;
        scene.PointerPress(20, 20, QtEnums.LeftButton, 0, true);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(0, root.ReadProperty("doubles"));
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string popup)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            import QtQuick.Controls
            Item {
              id: root
              width: 200; height: 200
              property var flyout: pop
              property int doubles: 0
              MouseArea {
                x: 10; y: 10; width: 40; height: 40
                onDoubleClicked: { doubles++ }
              }
              Popup { id: pop; {{popup}}; x: 120; y: 120; width: 40; height: 40; parent: root }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(200);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        ((Popup)root.ReadProperty("flyout")!).Open();
        shell.Loop.Process();
        return (root, scene, shell);
    }
}
