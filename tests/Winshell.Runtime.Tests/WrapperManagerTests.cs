using Winshell.Runtime.Quick;
using Winshell.Runtime.Quickshell;

namespace Winshell.Runtime.Tests;

public class WrapperManagerTests
{
    [Fact]
    public void One_visual_child_is_the_wrapped_child()
    {
        var root = (Item)TestShell.Run("""
            import QtQuick
            import Quickshell.Widgets
            Item {
              WrapperManager { }
              Text { objectName: "only"; text: "only" }
            }
            """, out var shell);
        using var _ = shell;
        var manager = Assert.IsType<WrapperManager>(Assert.Single(root.Resources));
        var child = Assert.Single(root.ChildItems);
        Assert.Equal("only", child.ObjectName);
        Assert.Same(child, manager.Child);
        Assert.Same(root, manager.Wrapper_);
        Assert.Same(root, child.ParentItem);
    }

    [Fact]
    public void Extra_visual_children_are_unparented_until_one_is_chosen()
    {
        var root = (Item)TestShell.Run("""
            import QtQuick
            import Quickshell.Widgets
            Item {
              WrapperManager { }
              Text { objectName: "a"; text: "a" }
              Text { objectName: "b"; text: "b" }
            }
            """, out var shell);
        using var _ = shell;
        var manager = Assert.IsType<WrapperManager>(Assert.Single(root.Resources));
        Assert.Null(manager.Child);
        Assert.Empty(root.ChildItems);
        var texts = root.OwnedObjects.OfType<Text>().OrderBy(t => t.ObjectName).ToList();
        Assert.Equal(["a", "b"], texts.Select(t => t.ObjectName));
        Assert.All(texts, text =>
        {
            Assert.Null(text.ParentItem);
            Assert.Same(root, text.Owner);
        });
        Assert.Contains(shell.Messages, message => message.Contains("more than one visual child", StringComparison.Ordinal));
    }

    [Fact]
    public void The_child_property_keeps_that_child_and_unparents_the_rest()
    {
        var root = (Item)TestShell.Run("""
            import QtQuick
            import Quickshell.Widgets
            Item {
              WrapperManager { child: a }
              Text { id: a; objectName: "a"; text: "a" }
              Text { objectName: "b"; text: "b" }
            }
            """, out var shell);
        using var _ = shell;
        var manager = Assert.IsType<WrapperManager>(Assert.Single(root.Resources));
        var kept = Assert.Single(root.ChildItems);
        Assert.Equal("a", kept.ObjectName);
        Assert.Same(kept, manager.Child);
        var dropped = Assert.Single(root.OwnedObjects.OfType<Text>(), t => t.ObjectName == "b");
        Assert.Null(dropped.ParentItem);
        Assert.Same(root, dropped.Owner);
    }

    [Fact]
    public void An_inline_child_is_parented_to_the_wrapper()
    {
        var root = (Item)TestShell.Run("""
            import QtQuick
            import Quickshell.Widgets
            Item {
              WrapperManager {
                child: Text { objectName: "inline"; text: "in" }
              }
            }
            """, out var shell);
        using var _ = shell;
        var manager = Assert.IsType<WrapperManager>(Assert.Single(root.Resources));
        var child = Assert.Single(root.ChildItems);
        Assert.Equal("inline", child.ObjectName);
        Assert.Same(child, manager.Child);
        Assert.Same(root, child.ParentItem);
    }

    [Fact]
    public void The_wrapper_cannot_change_after_initialization()
    {
        var root = (Item)TestShell.Run("""
            import QtQuick
            import Quickshell.Widgets
            Item {
              id: root
              WrapperManager { id: manager }
              Text { objectName: "only"; text: "only" }
              Component.onCompleted: manager.wrapper = root
            }
            """, out var shell);
        using var _ = shell;
        var manager = Assert.IsType<WrapperManager>(Assert.Single(root.Resources));
        Assert.Same(root, manager.Wrapper_);
        Assert.Same(Assert.Single(root.ChildItems), manager.Child);
        Assert.Contains(shell.Messages, message => message.Contains("Cannot set wrapper after", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wrapper_outside_an_item_stays_empty()
    {
        var root = TestShell.Run("""
            import QtQml
            import Quickshell.Widgets
            QtObject {
              property var manager: WrapperManager { }
              property var wrapped: manager.child
            }
            """, out var shell);
        using var _ = shell;
        Assert.Null(root.ReadProperty("wrapped"));
        Assert.Contains(shell.Messages, message => message.Contains("Parent of WrapperManager is not an Item", StringComparison.Ordinal));
    }
}
