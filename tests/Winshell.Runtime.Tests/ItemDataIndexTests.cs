using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ItemDataIndexTests
{
    [Fact]
    public void Data_lists_children_then_resources_and_shifts_when_a_child_is_added()
    {
        var owner = new Item();
        var first = new Item();
        var resource = new QObject();
        var second = new Item();
        owner.Data.Append(first);
        owner.Data.Append(resource);
        owner.Data.Append(second);

        Assert.Equal(3, owner.Data.Count);
        Assert.Same(first, owner.Data[0]);
        Assert.Same(second, owner.Data[1]);
        Assert.Same(resource, owner.Data[2]);
        Assert.Equal([first, second, resource], owner.Data);

        var third = new Item();
        owner.Data.Append(third);

        Assert.Equal(4, owner.Data.Count);
        Assert.Same(third, owner.Data[2]);
        Assert.Same(resource, owner.Data[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Data[4]);
    }
}
