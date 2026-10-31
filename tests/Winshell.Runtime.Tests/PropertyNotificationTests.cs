using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A binding on these properties sees a change, rather than keeping its first value.</summary>
public class PropertyNotificationTests
{
    [Theory]
    [InlineData("topLeftRadius")]
    [InlineData("topRightRadius")]
    [InlineData("bottomLeftRadius")]
    [InlineData("bottomRightRadius")]
    public void A_binding_sees_a_region_corner_radius_change(string name)
    {
        using var shell = new TestShell(("shell.qml", $$"""
            import Winshell
            ShellRoot {
              Region { id: area }
              property real seen: area.{{name}}
              function change() { area.{{name}} = 7 }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "change()");

        Assert.Equal(7.0, shell.Eval(root, "seen"));
    }

    [Theory]
    [InlineData("topMargin")]
    [InlineData("bottomMargin")]
    [InlineData("leftMargin")]
    [InlineData("rightMargin")]
    [InlineData("extraMargin")]
    [InlineData("implicitWidth")]
    [InlineData("implicitHeight")]
    public void A_binding_sees_a_margin_manager_property_change(string name)
    {
        using var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            import Winshell.Widgets
            Item {
              MarginWrapperManager { id: manager }
              property real seen: manager.{{name}}
              function change() { manager.{{name}} = 7 }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "change()");

        Assert.Equal(7.0, shell.Eval(root, "seen"));
    }

    [Fact]
    public void A_side_with_no_margin_of_its_own_follows_margin_and_one_with_its_own_does_not()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import Winshell.Widgets
            Item {
              MarginWrapperManager { id: manager; leftMargin: 3 }
              property real top: manager.topMargin
              property real left: manager.leftMargin
              function change() { manager.margin = 7 }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "change()");

        Assert.Equal(7.0, shell.Eval(root, "top"));
        Assert.Equal(3.0, shell.Eval(root, "left"));
    }

    [Fact]
    public void A_binding_sees_resize_child_change()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import Winshell.Widgets
            Item {
              MarginWrapperManager { id: manager }
              property bool seen: manager.resizeChild
              function change() { manager.resizeChild = false }
            }
            """));
        var root = shell.Load();

        shell.Eval(root, "change()");

        Assert.Equal(false, shell.Eval(root, "seen"));
    }
}
