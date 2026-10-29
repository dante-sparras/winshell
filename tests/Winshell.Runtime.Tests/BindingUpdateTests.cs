namespace Winshell.Runtime.Tests;

public class BindingUpdateTests
{
    [Fact]
    public void A_binding_drops_a_dependency_it_no_longer_reads()
    {
        var source = new QObject();
        var other = new QObject { ObjectName = "other" };
        var useOther = false;
        var target = new QObject();
        var binding = new Binding(target, "objectName", () => useOther ? other.ObjectName : source.ObjectName, "binding");
        binding.Evaluate();
        Assert.Equal(1, source.ObserverCount("objectName"));
        Assert.Equal(0, other.ObserverCount("objectName"));

        useOther = true;
        source.ObjectName = "a";
        Assert.Equal("other", target.ObjectName);
        Assert.Equal(0, source.ObserverCount("objectName"));
        Assert.Equal(1, other.ObserverCount("objectName"));
        binding.Dispose();
        Assert.Equal(0, other.ObserverCount("objectName"));
    }

    [Fact]
    public void Removing_an_observer_during_notification_still_tells_the_rest()
    {
        var source = new QObject();
        var seen = new List<string>();
        var first = new RecordingObserver(seen, "first", source);
        var second = new RecordingObserver(seen, "second", null);
        source.AddObserver("objectName", first);
        source.AddObserver("objectName", second);
        source.ObjectName = "x";
        Assert.Equal(["first", "second"], seen);
        Assert.Equal(1, source.ObserverCount("objectName"));
    }

    [Fact]
    public void An_observer_added_during_notification_waits_for_the_next_change()
    {
        var source = new QObject();
        var seen = new List<string>();
        IPropertyObserver? added = null;
        var first = new RecordingObserver(seen, "first", null, () =>
        {
            added = new RecordingObserver(seen, "added", null);
            source.AddObserver("objectName", added);
        });
        source.AddObserver("objectName", first);
        source.ObjectName = "x";
        Assert.Equal(["first"], seen);
        source.ObjectName = "y";
        Assert.Equal(["first", "first", "added"], seen);
    }

    private sealed class RecordingObserver(List<string> seen, string name, QObject? removeFrom, Action? during = null) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property)
        {
            seen.Add(name);
            during?.Invoke();
            removeFrom?.RemoveObserver(property, this);
        }
    }
}
