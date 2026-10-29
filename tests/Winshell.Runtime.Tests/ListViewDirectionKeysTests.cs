using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A ListView lays out bottom to top or right to left, and the arrow keys move its current item.</summary>
public class ListViewDirectionKeysTests
{
    private const int Up = 0x01000013, Down = 0x01000015, Left = 0x01000012, Right = 0x01000014;

    private static (ListView List, Scene Scene, TestShell Shell) Show(string properties, int count = 3)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            ListView {
              width: 200; height: 200
              model: {{count}}
              delegate: Rectangle { width: 30; height: 30 }
              {{properties}}
            }
            """));
        var list = (ListView)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(400);
        scene.Root.SetLayoutHeight(400);
        list.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (list, scene, shell);
    }

    private static bool Press(Scene scene, int key)
    {
        var taken = scene.KeyPress(new KeyEvent { Key = key });
        scene.KeyRelease(new KeyEvent { Key = key });
        PolishQueue.Flush();
        return taken;
    }

    private static double[] Ys(ListView list) => Enumerable.Range(0, list.Count).Select(i => list.ItemAtIndex(i)!.Y).ToArray();

    [Fact]
    public void Bottom_to_top_puts_item_0_at_the_bottom_and_stacks_later_items_up()
    {
        var (list, _, shell) = Show("verticalLayoutDirection: ListView.BottomToTop; spacing: 10");
        using var _ = shell;

        Assert.Equal([170, 130, 90], Ys(list));
    }

    [Fact]
    public void Bottom_to_top_with_more_rows_than_fit_opens_on_item_0()
    {
        var (list, _, shell) = Show("verticalLayoutDirection: ListView.BottomToTop", count: 10);
        using var _ = shell;

        Assert.Equal(270, list.ItemAtIndex(0)!.Y);
        Assert.Equal(0, list.ItemAtIndex(9)!.Y);
        Assert.Equal(100, list.ContentY);
    }

    [Fact]
    public void Right_to_left_puts_item_0_at_the_right_of_a_horizontal_list()
    {
        var (list, _, shell) = Show("orientation: ListView.Horizontal; layoutDirection: Qt.RightToLeft");
        using var _ = shell;

        Assert.Equal([170, 140, 110], Enumerable.Range(0, 3).Select(i => list.ItemAtIndex(i)!.X));
    }

    [Fact]
    public void Arrow_keys_move_the_current_index_and_stop_at_the_ends()
    {
        var (list, scene, shell) = Show("");
        using var _ = shell;
        list.ForceActiveFocus();

        Assert.True(Press(scene, Down));
        Assert.True(Press(scene, Down));
        Assert.Equal(2, list.CurrentIndex);
        Assert.False(Press(scene, Down));
        Assert.Equal(2, list.CurrentIndex);
        Assert.False(Press(scene, Right));
        Assert.True(Press(scene, Up));
        Assert.Equal(1, list.CurrentIndex);
    }

    [Fact]
    public void Arrow_keys_wrap_when_key_navigation_wraps()
    {
        var (list, scene, shell) = Show("keyNavigationWraps: true");
        using var _ = shell;
        list.ForceActiveFocus();

        Assert.True(Press(scene, Up));
        Assert.Equal(2, list.CurrentIndex);
        Assert.True(Press(scene, Down));
        Assert.Equal(0, list.CurrentIndex);
    }

    [Fact]
    public void Arrow_keys_follow_the_layout_direction()
    {
        var (list, scene, shell) = Show("verticalLayoutDirection: ListView.BottomToTop");
        using var _ = shell;
        list.ForceActiveFocus();
        Assert.True(Press(scene, Up));
        Assert.Equal(1, list.CurrentIndex);

        var (row, rowScene, rowShell) = Show("orientation: ListView.Horizontal; layoutDirection: Qt.RightToLeft");
        using var __ = rowShell;
        row.ForceActiveFocus();
        Assert.True(Press(rowScene, Left));
        Assert.Equal(1, row.CurrentIndex);
    }

    [Fact]
    public void Arrow_keys_bring_the_current_item_into_view()
    {
        var (list, scene, shell) = Show("height: 100", count: 10);
        using var _ = shell;
        list.ForceActiveFocus();

        for (var i = 0; i < 5; i++)
            Press(scene, Down);

        Assert.Equal(5, list.CurrentIndex);
        Assert.Equal(80, list.ContentY);
    }

    [Theory]
    [InlineData("keyNavigationEnabled: false")]
    [InlineData("interactive: false")]
    public void Arrow_keys_do_nothing_without_key_navigation(string properties)
    {
        var (list, scene, shell) = Show(properties);
        using var _ = shell;
        list.ForceActiveFocus();

        Assert.False(Press(scene, Down));
        Assert.Equal(0, list.CurrentIndex);
    }

    [Fact]
    public void Key_navigation_set_on_a_list_that_is_not_interactive_still_moves()
    {
        var (list, scene, shell) = Show("interactive: false; keyNavigationEnabled: true");
        using var _ = shell;
        list.ForceActiveFocus();

        Assert.True(Press(scene, Down));
        Assert.Equal(1, list.CurrentIndex);
    }
}
