using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>A menu opened at a point opens the way Windows menus do: flipped to fit, and with the keyboard on it.</summary>
public class MenuPopupTests
{
    private const string Qml = """
        import QtQuick
        import QtQuick.Controls
        Item {
          id: root
          width: 300; height: 300
          property var menu: menu
          TextInput { objectName: "field"; text: "ab"; width: 80; height: 24 }
          Menu {
            id: menu
            width: 150
            MenuItem { objectName: "copy"; text: "Copy" }
            MenuItem { objectName: "paste"; text: "Paste" }
          }
        }
        """;

    [Fact]
    public void A_menu_that_does_not_fit_right_of_the_point_opens_to_its_left()
    {
        var (root, scene, shell) = Show(Qml);
        using var _ = shell;
        var menu = (Menu)root.ReadProperty("menu")!;
        menu.Popup(250.0, 20.0);
        shell.Loop.Process();
        PolishQueue.Flush();
        Assert.Equal(100, menu.PopupItem.X);
        Assert.Equal(20, menu.PopupItem.Y);
    }

    [Fact]
    public void A_menu_that_does_not_fit_below_the_point_opens_above_it()
    {
        var (root, scene, shell) = Show(Qml);
        using var _ = shell;
        var menu = (Menu)root.ReadProperty("menu")!;
        menu.Popup(20.0, 250.0);
        shell.Loop.Process();
        PolishQueue.Flush();
        Assert.Equal(20, menu.PopupItem.X);
        Assert.Equal(250 - menu.PopupItem.Height, menu.PopupItem.Y);
    }

    [Fact]
    public void A_menu_that_fits_neither_way_is_kept_inside_the_window()
    {
        var (root, scene, shell) = Show(Qml);
        using var _ = shell;
        var menu = (Menu)root.ReadProperty("menu")!;
        menu.Width = 200;
        menu.Popup(150.0, 20.0);
        shell.Loop.Process();
        PolishQueue.Flush();
        Assert.Equal(100, menu.PopupItem.X);
    }

    [Fact]
    public void A_context_menu_opened_with_the_pointer_takes_the_keys()
    {
        var (root, scene, shell) = Show(Qml);
        using var _ = shell;
        var field = Find(root, "field");
        scene.SetFocusItem(field);
        Assert.False(scene.FocusVisible);
        var menu = (Menu)root.ReadProperty("menu")!;
        menu.Popup();
        shell.Loop.Process();
        Assert.True(menu.Opened);
        Assert.NotSame(field, scene.FocusItem);

        // Down enters the menu at its first item, as in a Windows context menu, then moves on.
        Press(scene, 0x01000015);
        Assert.Same(menu.ItemAt(0), scene.FocusItem);
        Press(scene, 0x01000015);
        Assert.Same(menu.ItemAt(1), scene.FocusItem);

        Press(scene, 0x01000000);
        Assert.False(menu.Opened);
        Assert.Same(field, scene.FocusItem);
    }

    [Fact]
    public void Up_enters_a_pointer_opened_menu_at_its_last_item()
    {
        var (root, scene, shell) = Show(Qml);
        using var _ = shell;
        var menu = (Menu)root.ReadProperty("menu")!;
        menu.Popup();
        shell.Loop.Process();
        Press(scene, 0x01000013);
        Assert.Same(menu.ItemAt(1), scene.FocusItem);
    }

    private static void Press(Scene scene, int key)
    {
        scene.KeyPress(new KeyEvent { Key = key });
        scene.KeyRelease(new KeyEvent { Key = key });
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(300);
        scene.Root.SetLayoutHeight(300);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static Item Find(Item root, string objectName)
    {
        if (root.ObjectName == objectName)
            return root;
        foreach (var child in root.ChildItems)
        {
            try
            {
                return Find(child, objectName);
            }
            catch (InvalidOperationException)
            {
            }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }
}
