using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A press on a text field takes focus only when its <c>activeFocusOnPress</c> is true.</summary>
public class ActiveFocusOnPressTests
{
    [Theory]
    [InlineData("TextInput")]
    [InlineData("TextField")]
    public void A_press_leaves_focus_where_it_was_when_active_focus_on_press_is_false(string type)
    {
        var (root, scene, shell) = Show(type, "activeFocusOnPress: false");
        using var _ = shell;
        var other = (Item)root.ReadProperty("other")!;
        var field = (TextInput)root.ReadProperty("field")!;
        Assert.Same(other, scene.FocusItem);

        scene.PointerPress(100, 15, QtEnums.LeftButton);
        scene.PointerRelease(100, 15, QtEnums.LeftButton);

        Assert.False(field.ActiveFocus);
        Assert.Same(other, scene.FocusItem);
        Assert.True(other.ActiveFocus);
    }

    [Theory]
    [InlineData("TextInput")]
    [InlineData("TextField")]
    public void A_press_takes_focus_by_default(string type)
    {
        var (root, scene, shell) = Show(type, "");
        using var _ = shell;
        var field = (TextInput)root.ReadProperty("field")!;

        scene.PointerPress(100, 15, QtEnums.LeftButton);
        scene.PointerRelease(100, 15, QtEnums.LeftButton);

        Assert.True(field.ActiveFocus);
        Assert.Same(field, scene.FocusItem);
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string type, string extra)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            import QtQuick.Controls
            Item {
              property Item field: input
              property Item other: second
              {{type}} { id: input; width: 200; height: 30; text: "hello"; {{extra}} }
              TextInput { id: second; y: 100; width: 200; height: 30; focus: true }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(200);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }
}
