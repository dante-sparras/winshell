using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Widget members from issue 137 either change the layout or fail to load.</summary>
public class WidgetMemberTests
{
    [Fact]
    public void A_visible_border_is_added_to_the_margins()
    {
        var box = (WrapperRectangle)TestShell.Run("""
            import QtQuick
            import Winshell.Widgets
            WrapperRectangle {
              extraMargin: 2
              border.width: 4
              border.color: "black"
              Rectangle { implicitWidth: 10; implicitHeight: 8; color: "red" }
            }
            """, out var shell);
        using var _ = shell;
        var child = Assert.Single(box.ChildItems);
        Assert.Equal(6, child.X);
        Assert.Equal(6, child.Y);
        Assert.Equal(22, box.ImplicitWidth);
        Assert.Equal(20, box.ImplicitHeight);
        box.Border.Width = 7;
        Assert.Equal(9, child.X);
        Assert.Equal(28, box.ImplicitWidth);
    }

    [Fact]
    public void The_child_keeps_its_margins_when_the_border_is_left_out()
    {
        var box = (WrapperRectangle)TestShell.Run("""
            import QtQuick
            import Winshell.Widgets
            WrapperRectangle {
              contentInsideBorder: false
              border.width: 4
              border.color: "black"
              Rectangle { implicitWidth: 10; implicitHeight: 8; color: "red" }
            }
            """, out var shell);
        using var _ = shell;
        var child = Assert.Single(box.ChildItems);
        Assert.Equal(0, child.X);
        Assert.Equal(10, box.ImplicitWidth);
        Assert.Equal(8, box.ImplicitHeight);
    }

    [Fact]
    public void Content_under_the_border_is_not_a_property()
    {
        Rejects("""
            import QtQuick
            import Winshell.Widgets
            ClippingRectangle { contentUnderBorder: true }
            """, "contentUnderBorder");
    }

    [Fact]
    public void The_manager_wrapper_is_its_item_and_its_implicit_size_wins()
    {
        var box = (Item)TestShell.Run("""
            import QtQuick
            import Winshell.Widgets
            Item {
              id: box
              property bool same: manager.wrapper === box
              MarginWrapperManager {
                id: manager
                margin: 5
                implicitWidth: 40
                implicitHeight: 30
              }
              Rectangle { implicitWidth: 10; implicitHeight: 8; color: "red" }
            }
            """, out var shell);
        using var _ = shell;
        Assert.True((bool)shell.Eval(box, "same")!);
        Assert.Equal(40, box.ImplicitWidth);
        Assert.Equal(30, box.ImplicitHeight);
        Assert.Equal(5, Assert.Single(box.ChildItems).X);
    }

    [Fact]
    public void The_old_wrapper_name_fails_to_load()
    {
        Rejects("""
            import QtQuick
            import Winshell.Widgets
            Item {
              MarginWrapperManager { wrapper_: parent }
            }
            """, "wrapper_");
    }

    private static void Rejects(string qml, string name)
    {
        using var shell = new TestShell(("shell.qml", qml));
        var ex = Assert.Throws<QmlCompileException>(() => shell.Load());
        Assert.Contains(name, ex.Message, StringComparison.Ordinal);
    }
}
