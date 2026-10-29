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

    [Fact]
    public void Set_inserts_new_rows_in_order_and_leaves_an_unchanged_list_alone()
    {
        var model = new ObjectModel();
        var rows = Enumerable.Range(0, 2000).Select(_ => new object()).ToList<object?>();
        var inserted = new List<int>();
        var signals = 0;
        model.Connect("objectInsertedPost", args => inserted.Add((int)args[1]!));
        model.ModelChanged += () => signals++;

        model.Set(rows);
        Assert.Equal(rows, model.Rows);
        Assert.Equal(Enumerable.Range(0, rows.Count), inserted);
        Assert.Equal(1, signals);

        inserted.Clear();
        signals = 0;
        model.Set(rows.ToList());
        Assert.Equal(rows, model.Rows);
        Assert.Empty(inserted);
        Assert.Equal(0, signals);

        var next = rows.Where((_, i) => i % 3 != 0).ToList();
        next.Insert(500, new object());
        next.Insert(0, null);
        (next[10], next[900]) = (next[900], next[10]);
        model.Set(next);
        Assert.Equal(next, model.Rows);
        Assert.Equal(1, signals);
    }

    [Fact]
    public void Set_keeps_a_repeated_object_at_its_first_place()
    {
        var model = new ObjectModel();
        var a = new object();
        var b = new object();
        model.Set([a, b, a, null, null]);
        Assert.Equal([a, b, null], model.Rows);
    }
}
