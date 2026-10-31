using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>SubtleButton is the keyboard and Narrator behavior the shell's quiet buttons share.</summary>
[Collection(ThemeCollection.Name)]
public class SubtleButtonTests
{
    private const string Qml = """
        import QtQuick
        Item {
          SubtleButton {
            objectName: "plain"
            x: 0; y: 0; width: 40; height: 40
            Accessible.name: "Plain"
            property int hits: 0
            onActivated: hits = hits + 1
          }
          SubtleButton {
            objectName: "shift"
            x: 0; y: 96; width: 40; height: 40
            shiftEnterActivates: false
            Accessible.name: "Shift stays here"
            property int hits: 0
            property int shifts: 0
            onActivated: hits = hits + 1
            Keys.onPressed: event => {
              event.accepted = false
              if ((event.key === Qt.Key_Return || event.key === Qt.Key_Enter) && (event.modifiers & Qt.ShiftModifier)) {
                shifts = shifts + 1
                event.accepted = true
              }
            }
          }
          SubtleButton {
            objectName: "ownPointer"
            x: 0; y: 144; width: 40; height: 40
            activateOnClick: false
            Accessible.name: "Own pointer"
            property int hits: 0
            property int clicks: 0
            onActivated: hits = hits + 1
            onClicked: clicks = clicks + 1
          }
          SubtleButton {
            objectName: "menuItem"
            x: 0; y: 192; width: 40; height: 40
            Accessible.role: Accessible.MenuItem
            Accessible.name: "Taskbar settings"
            property int hits: 0
            onActivated: hits = hits + 1
          }
        }
        """;

    private const int Return = 0x01000004, Enter = 0x01000005, Space = 0x20, Left = 0x01000012;

    private static int Number(DefaultShellHarness shell, string name, string property) =>
        Convert.ToInt32(shell.Find(name).ReadProperty(property));

    private static void Press(DefaultShellHarness shell, string name, int key, int modifiers = 0)
    {
        shell.Find(name).ForceActiveFocus(QtEnums.TabFocusReason);
        shell.Scene.KeyPress(new KeyEvent { Key = key, Modifiers = modifiers });
        shell.Scene.KeyRelease(new KeyEvent { Key = key, Modifiers = modifiers });
        shell.Settle();
    }

    [Fact]
    public void Return_numpad_Enter_Space_and_Narrator_each_activate_once()
    {
        using var shell = new DefaultShellHarness(Qml);

        Press(shell, "plain", Return);
        Press(shell, "plain", Enter);
        Press(shell, "plain", Space);
        Press(shell, "plain", Left);
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        var node = tree.Find(shell.Find("plain"))!;
        Assert.Equal(AccessibleAttached.RoleValue.Button, node.Role);
        Assert.Equal("Plain", node.Name);
        Assert.True(node.CanInvoke);
        node.Invoke();
        shell.Settle();

        Assert.Equal(4, Number(shell, "plain", "hits"));
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Shift_Return_stays_with_the_caller_when_the_button_does_not_take_it()
    {
        using var shell = new DefaultShellHarness(Qml);

        Press(shell, "shift", Return, QtEnums.ShiftModifier);
        Press(shell, "shift", Enter, QtEnums.ShiftModifier);
        Press(shell, "shift", Return);
        Press(shell, "shift", Space, QtEnums.ShiftModifier);

        Assert.Equal(2, Number(shell, "shift", "shifts"));
        Assert.Equal(2, Number(shell, "shift", "hits"));
    }

    [Fact]
    public void A_click_activates_once_and_a_button_with_its_own_pointer_does_not()
    {
        using var shell = new DefaultShellHarness(Qml);

        shell.Click(shell.Find("plain"));
        shell.Click(shell.Find("ownPointer"));

        Assert.Equal(1, Number(shell, "plain", "hits"));
        Assert.Equal(0, Number(shell, "ownPointer", "hits"));
        Assert.Equal(1, Number(shell, "ownPointer", "clicks"));
    }

    [Fact]
    public void A_menu_item_keeps_that_role_and_still_invokes()
    {
        using var shell = new DefaultShellHarness(Qml);
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        var node = tree.Find(shell.Find("menuItem"))!;

        Assert.Equal(AccessibleAttached.RoleValue.MenuItem, node.Role);
        Assert.Equal("Taskbar settings", node.Name);
        node.Invoke();
        shell.Settle();

        Assert.Equal(1, Number(shell, "menuItem", "hits"));
    }
}
