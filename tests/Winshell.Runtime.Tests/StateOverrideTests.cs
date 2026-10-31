using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class StateOverrideTests
{
    [Fact]
    public void A_state_overrides_a_binding_and_restores_it_live()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: root
              property real scale: 2
              Rectangle {
                id: r
                objectName: "r"
                width: root.scale * 10
                states: State { name: "fixed"; PropertyChanges { target: r; width: 5 } }
              }
            }
            """, out var shell);
        using var _ = shell;
        var rect = Find(root, "r");
        Assert.Equal(20.0, rect.Width);
        rect.State = "fixed";
        Assert.Equal(5.0, rect.Width);
        root.WriteProperty("scale", 4);
        Assert.Equal(5.0, rect.Width);
        rect.State = "";
        Assert.Equal(40.0, rect.Width);
    }

    [Fact]
    public void PropertyChanges_keeps_following_its_expression()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: root
              property real outer: 100
              Rectangle {
                id: r
                objectName: "r"
                width: 10
                states: State { name: "half"; PropertyChanges { target: r; width: root.outer / 2 } }
              }
            }
            """, out var shell);
        using var _ = shell;
        var rect = Find(root, "r");
        rect.State = "half";
        Assert.Equal(50.0, rect.Width);
        root.WriteProperty("outer", 200);
        Assert.Equal(100.0, rect.Width);
        rect.State = "";
        Assert.Equal(10.0, rect.Width);
    }

    [Fact]
    public void A_state_change_runs_a_behavior_when_no_transition_covers_it()
    {
        var root = TestShell.Run("""
            import QtQuick
            Rectangle {
              id: r
              width: 10
              Behavior on width { NumberAnimation { duration: 100 } }
              states: State { name: "big"; PropertyChanges { target: r; width: 110 } }
            }
            """, out var shell);
        using var _ = shell;
        var rect = (Item)root;
        rect.State = "big";
        Assert.Equal(10.0, rect.Width);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(50));
        Assert.InRange(rect.Width, 30.0, 90.0);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(110.0, rect.Width);
    }

    [Fact]
    public void Returning_to_the_default_state_runs_its_transition()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: box
              width: 10
              states: State { name: "wide"; PropertyChanges { target: box; width: 110 } }
              transitions: Transition {
                from: "wide"; to: ""
                NumberAnimation { properties: "width"; duration: 100 }
              }
            }
            """, out var shell);
        using var _ = shell;
        var box = (Item)root;
        box.State = "wide";
        Assert.Equal(110.0, box.Width);
        box.State = "";
        Assert.Equal(110.0, box.Width);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(10.0, box.Width);
    }

    [Fact]
    public void A_reversible_transition_also_runs_back_to_the_default_state()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: box
              width: 10
              states: State { name: "wide"; PropertyChanges { target: box; width: 110 } }
              transitions: Transition {
                from: ""; to: "wide"; reversible: true
                NumberAnimation { properties: "width"; duration: 100 }
              }
            }
            """, out var shell);
        using var _ = shell;
        var box = (Item)root;
        box.State = "wide";
        shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(110.0, box.Width);
        box.State = "";
        Assert.InRange(box.Width, 100.0, 120.0);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(10.0, box.Width);
    }

    [Fact]
    public void A_transition_between_states_starts_from_the_value_on_screen()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: box
              width: 10
              states: [
                State { name: "a"; PropertyChanges { target: box; width: 110 } },
                State { name: "b"; PropertyChanges { target: box; width: 210 } }
              ]
              transitions: Transition { NumberAnimation { properties: "width"; duration: 100 } }
            }
            """, out var shell);
        using var _ = shell;
        var box = (Item)root;
        box.State = "a";
        shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(110.0, box.Width);
        box.State = "b";
        Assert.InRange(box.Width, 100.0, 120.0);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(210.0, box.Width);
    }

    [Fact]
    public void restoreEntryValues_false_leaves_the_states_value_in_place()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: box
              width: 10
              states: State {
                name: "wide"
                PropertyChanges { target: box; width: 80; restoreEntryValues: false }
              }
            }
            """, out var shell);
        using var _ = shell;
        var box = (Item)root;
        box.State = "wide";
        Assert.Equal(80.0, box.Width);
        box.State = "";
        Assert.Equal(80.0, box.Width);
    }

    [Fact]
    public void A_state_write_that_fails_is_logged()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              id: box
              states: State { name: "bad"; PropertyChanges { target: box; childrenRect: 1 } }
            }
            """, out var shell);
        using var _ = shell;
        ((Item)root).State = "bad";
        Assert.Contains(shell.Messages, m => m.Contains("childrenRect", StringComparison.Ordinal));
    }

    private static Item Find(QObject root, string objectName)
    {
        if (root is Item item && item.ObjectName == objectName)
            return item;
        foreach (var child in root.OwnedObjects)
        {
            try
            {
                return Find(child, objectName);
            }
            catch (InvalidOperationException)
            {
            }
        }
        if (root is Item parent)
        {
            foreach (var child in parent.ChildItems)
            {
                try
                {
                    return Find(child, objectName);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }
}
