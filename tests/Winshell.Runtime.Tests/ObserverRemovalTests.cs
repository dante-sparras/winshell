namespace Winshell.Runtime.Tests;

public class ObserverRemovalTests
{
    [Fact]
    public void Removing_the_first_of_two_registrations_keeps_the_later_one()
    {
        var source = new QObject();
        var seen = new List<string>();
        var first = new NamedObserver(seen, "a");
        var second = new NamedObserver(seen, "b");
        source.AddObserver("objectName", first);
        source.AddObserver("objectName", second);
        source.AddObserver("objectName", first);

        source.RemoveObserver("objectName", first);
        source.ObjectName = "x";

        Assert.Equal(["b", "a"], seen);
        Assert.Equal(2, source.ObserverCount("objectName"));
    }

    [Fact]
    public void Compaction_preserves_notification_order()
    {
        var source = new QObject();
        var seen = new List<string>();
        var keep = new NamedObserver(seen, "keep");
        var drop = new NamedObserver(seen, "drop");
        source.AddObserver("objectName", drop);
        source.AddObserver("objectName", keep);
        source.AddObserver("objectName", drop);
        source.AddObserver("objectName", keep);

        source.RemoveObserver("objectName", drop);
        source.RemoveObserver("objectName", drop);
        source.ObjectName = "x";

        Assert.Equal(["keep", "keep"], seen);
    }

    [Fact]
    public void Removing_many_observers_does_not_compare_them()
    {
        const int count = 64;
        var source = new QObject();
        var observers = new CountingObserver[count];
        for (var i = 0; i < count; i++)
        {
            observers[i] = new CountingObserver();
            source.AddObserver("objectName", observers[i]);
        }

        for (var i = count - 1; i >= 0; i--)
            source.RemoveObserver("objectName", observers[i]);

        var comparisons = 0;
        foreach (var observer in observers)
            comparisons += observer.Comparisons;
        Assert.Equal(0, comparisons);
        Assert.Equal(0, source.ObserverCount("objectName"));
    }

    private sealed class NamedObserver(List<string> seen, string name) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => seen.Add(name);
    }

    private sealed class CountingObserver : IPropertyObserver
    {
        public int Comparisons { get; private set; }

        public void OnPropertyChanged(QObject sender, string property)
        {
        }

        public override bool Equals(object? obj)
        {
            Comparisons++;
            return ReferenceEquals(this, obj);
        }

        public override int GetHashCode() => base.GetHashCode();
    }
}
