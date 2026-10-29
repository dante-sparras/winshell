using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class ContainerCurrentTests
{
    [Fact]
    public void Removing_the_current_tab_checks_the_one_that_takes_its_place()
    {
        var bar = new TabBar();
        var first = new TabButton();
        var second = new TabButton();
        var third = new TabButton();
        bar.AddItem(first);
        bar.AddItem(second);
        bar.AddItem(third);

        bar.RemoveItem(first);

        Assert.Equal(0, bar.CurrentIndex);
        Assert.Same(second, bar.CurrentItem);
        Assert.True(second.Checked);
        Assert.False(third.Checked);
    }

    [Fact]
    public void Removing_a_middle_tab_selects_the_previous_one()
    {
        var bar = new TabBar();
        var first = new TabButton();
        var second = new TabButton();
        var third = new TabButton();
        bar.AddItem(first);
        bar.AddItem(second);
        bar.AddItem(third);
        bar.CurrentIndex = 1;

        bar.RemoveItem(second);

        Assert.Equal(0, bar.CurrentIndex);
        Assert.Same(first, bar.CurrentItem);
        Assert.True(first.Checked);
        Assert.False(third.Checked);
    }

    [Fact]
    public void Moving_the_current_tab_keeps_it_current()
    {
        var bar = new TabBar();
        var first = new TabButton();
        var second = new TabButton();
        var third = new TabButton();
        bar.AddItem(first);
        bar.AddItem(second);
        bar.AddItem(third);

        bar.MoveItem(0, 2);

        Assert.Equal(2, bar.CurrentIndex);
        Assert.Same(first, bar.CurrentItem);
        Assert.True(first.Checked);
        Assert.Equal([second, third, first], bar.Items);
    }

    [Fact]
    public void Moving_another_tab_across_the_current_one_follows_it()
    {
        var bar = new TabBar();
        var first = new TabButton();
        var second = new TabButton();
        var third = new TabButton();
        bar.AddItem(first);
        bar.AddItem(second);
        bar.AddItem(third);

        bar.MoveItem(2, 0);

        Assert.Equal(1, bar.CurrentIndex);
        Assert.Same(first, bar.CurrentItem);
        Assert.True(first.Checked);
    }

    [Fact]
    public void Pop_null_unwinds_to_the_first_page()
    {
        var stack = new StackView();
        var first = new Item();
        var second = new Item();
        var third = new Item();
        stack.Push(first, second, third);

        var popped = stack.Pop(new object?[] { null });

        Assert.Same(second, popped);
        Assert.Equal(1, stack.Depth);
        Assert.Same(first, stack.CurrentItem);
        Assert.Null(stack.Pop());
        Assert.Equal(1, stack.Depth);
    }

    [Fact]
    public void Pop_without_an_argument_removes_one_page()
    {
        var stack = new StackView();
        var first = new Item();
        var second = new Item();
        var third = new Item();
        stack.Push(first, second, third);

        Assert.Same(third, stack.Pop());
        Assert.Equal(2, stack.Depth);
        Assert.Same(second, stack.CurrentItem);
    }
}
