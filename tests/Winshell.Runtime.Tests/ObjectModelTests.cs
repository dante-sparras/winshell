using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class ObjectModelTests
{
    [Fact]
    public void Set_publishes_once_and_reuses_the_value_list_until_the_next_change()
    {
        var model = new ObjectModel();
        var a = new object();
        var b = new object();
        var c = new object();
        var signals = 0;
        model.ModelChanged += () => signals++;

        model.Set([a, b, c]);
        Assert.Equal(1, signals);
        Assert.Equal([a, b, c], model.Rows);

        var published = model.Values;
        Assert.Same(published, model.Values);

        signals = 0;
        model.Set([c, a]);
        Assert.Equal(1, signals);
        Assert.Equal([c, a], model.Rows);
        Assert.NotSame(published, model.Values);

        signals = 0;
        model.Set([c, a]);
        Assert.Equal(0, signals);
        Assert.Same(model.Values, model.Values);

        var extra = new object();
        model.Set([extra]);
        Assert.Equal([extra], model.Rows);
        Assert.DoesNotContain(a, model.Rows);
    }

    [Fact]
    public void Set_of_a_repeated_reference_does_not_throw()
    {
        var model = new ObjectModel();
        var a = new object();
        model.Set([a, a]);
        Assert.Equal([a], model.Rows);
        model.Set([a]);
        Assert.Equal([a], model.Rows);
    }
}
