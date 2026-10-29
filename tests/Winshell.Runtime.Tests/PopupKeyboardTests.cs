using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class PopupKeyboardTests
{
    [Fact]
    public void A_modal_popup_opened_with_the_pointer_takes_focus()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              id: root
              property var flyout: pop
              TextInput { id: field; objectName: "field"; text: "ab"; width: 80; height: 24 }
              Popup { id: pop; modal: true; width: 40; height: 40; parent: root }
            }
            """);
        using var _ = shell;
        var field = (TextInput)Find(root, "field");
        scene.SetFocusItem(field);
        Assert.False(scene.FocusVisible);
        var popup = (Popup)root.ReadProperty("flyout")!;
        popup.Open();
        shell.Loop.Process();
        Assert.True(popup.Opened);
        Assert.NotSame(field, scene.FocusItem);
        Press(scene, 0x01000003);
        Assert.Equal("ab", field.ReadProperty("text"));
    }

    [Fact]
    public void A_mouse_opened_list_moves_the_highlight_and_commits_on_enter()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ComboBox {
              objectName: "box"
              x: 10; y: 10; width: 140; height: 36
              model: ["a", "b", "c"]
              currentIndex: 1
            }
            """);
        using var _ = shell;
        var box = (ComboBox)Find(root, "box");
        Click(scene, 40, 20);
        shell.Loop.Process();
        Assert.True(box.IsPopupOpen);
        Assert.False(scene.FocusVisible);
        scene.SetFocusItem(box);
        Assert.Equal(1, box.CurrentIndex);
        // The row under the pointer may take the highlight. Two steps still land on the last entry,
        // and the current entry stays put until Enter.
        Press(scene, 0x01000015);
        Press(scene, 0x01000015);
        Assert.Equal(1, box.CurrentIndex);
        Assert.Equal(2, box.HighlightedIndex);
        Press(scene, 0x01000004);
        Assert.Equal(2, box.CurrentIndex);
        Assert.False(box.IsPopupOpen);
    }

    [Fact]
    public void A_keyboard_opened_list_focuses_the_current_row()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ComboBox {
              objectName: "box"
              x: 10; y: 10; width: 140; height: 36
              model: ["a", "b", "c"]
              currentIndex: 1
            }
            """);
        using var _ = shell;
        var box = (ComboBox)Find(root, "box");
        scene.MoveFocus(box);
        Press(scene, 0x20);
        shell.Loop.Process();
        Assert.True(box.IsPopupOpen);
        Assert.Equal(1, box.CurrentIndex);
        Assert.Equal(1, box.HighlightedIndex);
        Assert.Same(box.HighlightedRow(), scene.FocusItem);
        Press(scene, 0x01000015);
        Assert.Equal(1, box.CurrentIndex);
        Assert.Equal(2, box.HighlightedIndex);
        Assert.Same(box.HighlightedRow(), scene.FocusItem);
        Press(scene, 0x01000004);
        Assert.Equal(2, box.CurrentIndex);
        Assert.False(box.IsPopupOpen);
    }

    private static void Press(Scene scene, int key)
    {
        scene.KeyPress(new KeyEvent { Key = key });
        scene.KeyRelease(new KeyEvent { Key = key });
    }

    private static void Click(Scene scene, double x, double y)
    {
        scene.PointerPress(x, y, QtEnums.LeftButton);
        scene.PointerRelease(x, y, QtEnums.LeftButton);
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
