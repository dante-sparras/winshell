using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>Menus, tool tips and popups against WinUI 3's menu flyout, tool tip and flyout presenter.</summary>
[Collection(ThemeCollection.Name)]
public class FluentFlyoutTests
{
    private static (Item Root, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(600);
        scene.Root.SetLayoutHeight(400);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, shell);
    }

    [Fact]
    public void A_menu_is_as_tall_as_its_rows_of_32_pixels()
    {
        var (root, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias menu: menu
              Menu {
                id: menu
                MenuItem { text: "Copy" }
                MenuItem { text: "Paste" }
                MenuSeparator { }
                MenuItem { text: "Delete"; down: true }
              }
              Component.onCompleted: menu.open()
            }
            """);
        using var _ = shell;
        shell.Loop.Process();
        PolishQueue.Flush();
        var menu = (Menu)root.ReadProperty("menu")!;
        // A 1 pixel border with 2 pixels of padding, three 32 pixel rows, and a 3 pixel separator.
        Assert.Equal(3 + 3 * 32 + 3 + 3, menu.PopupItem.ImplicitHeight);
        var pressed = (MenuItem)menu.ItemAt(3)!;
        Assert.Equal(Theme.Current.TextFillColorPrimary, pressed.ContentColor);
    }

    [Fact]
    public void A_tool_tip_wraps_at_320_pixels_with_its_border()
    {
        var (root, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias tip: tip
              ToolTip { id: tip; text: "A longer tool tip that wraps onto a second line because it is wide, and then some more" }
              Component.onCompleted: tip.open()
            }
            """);
        using var _ = shell;
        var tip = (ToolTip)root.ReadProperty("tip")!;
        Assert.Equal(320, tip.PopupItem.ImplicitWidth);
    }

    [Fact]
    public void Opening_a_combo_box_popup_directly_shows_its_rows()
    {
        var (root, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ComboBox { width: 200; model: ["Apple", "Pear", "Plum"]; Component.onCompleted: popup.open() }
            """);
        using var _ = shell;
        var combo = (Quick.Controls.ComboBox)root;
        var popup = (Popup)combo.ReadProperty("popup")!;
        Assert.True(popup.Opened);
        Assert.Equal(200, popup.PopupItem.Width);
        Assert.Equal(2 + 3 * 40 + 2, popup.PopupItem.Height);
    }

    [Fact]
    public void Padding_set_in_qml_wins_over_the_style_on_every_side()
    {
        var (root, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              Button { text: "A" }
              Button { text: "B"; padding: 2 }
              Button { text: "C"; padding: 2; leftPadding: 5 }
              Button { text: "D"; horizontalPadding: 0 }
            }
            """);
        using var _ = shell;
        var b = root.ChildItems.Cast<Control>().ToArray();
        Assert.Equal([12.0, 6, 12, 7], [b[0].LeftPadding, b[0].TopPadding, b[0].RightPadding, b[0].BottomPadding]);
        Assert.Equal([2.0, 2, 2, 2], [b[1].LeftPadding, b[1].TopPadding, b[1].RightPadding, b[1].BottomPadding]);
        Assert.Equal([5.0, 2, 2, 2], [b[2].LeftPadding, b[2].TopPadding, b[2].RightPadding, b[2].BottomPadding]);
        Assert.Equal([0.0, 6, 0, 7], [b[3].LeftPadding, b[3].TopPadding, b[3].RightPadding, b[3].BottomPadding]);
    }
}
