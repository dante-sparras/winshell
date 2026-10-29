using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>Bindings on control properties that change without a write to them.</summary>
public class ControlPropertyNotifyTests
{
    private static (Item Root, TestShell Shell) Load(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(400);
        scene.Root.SetLayoutHeight(300);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, shell);
    }

    [Fact]
    public void A_combo_box_current_text_and_value_follow_their_roles()
    {
        var (root, shell) = Load("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias box: box
              property string shown: box.currentText
              property var value: box.currentValue
              ComboBox {
                id: box
                textRole: "a"
                valueRole: "a"
                model: [{ a: "first", b: "second" }]
              }
            }
            """);
        using var _ = shell;
        Assert.Equal("first", root.ReadProperty("shown"));
        Assert.Equal("first", root.ReadProperty("value"));

        var box = (ComboBox)root.ReadProperty("box")!;
        box.TextRole = "b";
        box.ValueRole = "b";

        Assert.Equal("second", root.ReadProperty("shown"));
        Assert.Equal("second", root.ReadProperty("value"));
    }

    [Fact]
    public void A_menu_count_follows_added_and_removed_items()
    {
        var (root, shell) = Load("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias menu: menu
              property int count: menu.count
              Menu {
                id: menu
                MenuItem { text: "Copy" }
              }
            }
            """);
        using var _ = shell;
        Assert.Equal(1, root.ReadProperty("count"));

        var menu = (Menu)root.ReadProperty("menu")!;
        var paste = new MenuItem();
        menu.AddItem(paste);
        Assert.Equal(2, root.ReadProperty("count"));

        menu.TakeItem(0);
        Assert.Equal(1, root.ReadProperty("count"));

        menu.RemoveItem(paste);
        Assert.Equal(0, root.ReadProperty("count"));
    }

    [Fact]
    public void A_menu_item_is_highlighted_while_hovered()
    {
        var (root, shell) = Load("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias item: item
              property bool lit: item.highlighted
              property int changes: 0
              Menu {
                MenuItem { id: item; text: "Copy"; onHighlightedChanged: changes++ }
              }
            }
            """);
        using var _ = shell;
        var item = (MenuItem)root.ReadProperty("item")!;
        Assert.Equal(false, root.ReadProperty("lit"));

        item.HoverEnter(1, 1, 0);
        Assert.Equal(true, root.ReadProperty("lit"));
        Assert.Equal(1, root.ReadProperty("changes"));

        item.HoverLeave();
        Assert.Equal(false, root.ReadProperty("lit"));
        Assert.Equal(2, root.ReadProperty("changes"));
    }

    [Fact]
    public void A_button_group_forgets_a_checked_button_that_is_unchecked()
    {
        var (root, shell) = Load("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias first: first
              property alias group: group
              property var current: group.checkedButton
              ButtonGroup { id: group }
              CheckBox { id: first; ButtonGroup.group: group }
              CheckBox { ButtonGroup.group: group }
            }
            """);
        using var _ = shell;
        var first = (AbstractButton)root.ReadProperty("first")!;
        var group = (ButtonGroup)root.ReadProperty("group")!;

        first.Checked = true;
        Assert.Same(first, root.ReadProperty("current"));

        first.Checked = false;
        Assert.Null(root.ReadProperty("current"));
        Assert.Null(group.CheckedButton);
    }
    [Fact]
    public void A_button_group_moves_straight_to_the_newly_checked_button()
    {
        var (root, shell) = Load("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias first: first
              property alias second: second
              property string seen: ""
              ButtonGroup { id: group; onCheckedButtonChanged: seen += (checkedButton ? checkedButton.text : "none") + ";" }
              CheckBox { id: first; text: "first"; checked: true; ButtonGroup.group: group }
              CheckBox { id: second; text: "second"; ButtonGroup.group: group }
            }
            """);
        using var _ = shell;
        var second = (AbstractButton)root.ReadProperty("second")!;

        second.Checked = true;

        Assert.False(((AbstractButton)root.ReadProperty("first")!).Checked);
        Assert.Equal("first;second;", root.ReadProperty("seen"));
    }
}
