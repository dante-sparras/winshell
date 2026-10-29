using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A Flickable scrolls by the Windows wheel setting and reports which way it moves.</summary>
[Collection(ThemeCollection.Name)]
public class FlickableWheelTests
{
    [Theory]
    [InlineData(3u, 48)]
    [InlineData(5u, 80)]
    [InlineData(uint.MaxValue, 100)]
    public void A_wheel_notch_scrolls_the_lines_windows_sets(uint lines, double expected)
    {
        var before = Theme.Current;
        try
        {
            Theme.Current = before with { WheelScrollLines = lines };
            var (flickable, loop, engine) = Make();
            using var _ = engine;

            Assert.True(flickable.Wheel(10, 10, 0, -120, 0, 0, 0, 0, false));
            loop.Advance(TimeSpan.FromSeconds(2));

            Assert.Equal(expected, flickable.ContentY);
        }
        finally
        {
            Theme.Current = before;
        }
    }

    [Fact]
    public void Content_that_shrinks_mid_scroll_settles_in_bounds()
    {
        var (flickable, loop, engine) = Make();
        using var _ = engine;

        Assert.True(flickable.Wheel(10, 10, 0, -1200, 0, 0, 0, 0, false));
        loop.Advance(AnimationClock.FrameInterval);
        flickable.ContentHeight = 150;
        loop.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(50, flickable.ContentY);
        Assert.False(flickable.Moving);
    }

    [Fact]
    public void Ctrl_wheel_is_left_for_a_parent()
    {
        var (flickable, loop, engine) = Make();
        using var _ = engine;

        Assert.False(flickable.Wheel(10, 10, 0, -120, 0, 0, 0, QtEnums.ControlModifier, false));
        loop.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(0, flickable.ContentY);
    }

    [Fact]
    public void Axis_properties_notify_for_the_axis_that_moves()
    {
        var (flickable, loop, engine) = Make();
        using var _ = engine;
        var seen = new List<string>();
        var observer = new Recorder(seen);
        string[] names = ["moving", "movingVertically", "movingHorizontally", "dragging", "draggingVertically", "draggingHorizontally"];
        foreach (var name in names)
            flickable.AddObserver(name, observer);

        flickable.PointerPress(10, 50, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        flickable.PointerMove(10, 20, QtEnums.LeftButton, 0);
        Assert.True(flickable.DraggingVertically);
        Assert.True(flickable.MovingVertically);
        Assert.False(flickable.DraggingHorizontally);
        Assert.False(flickable.MovingHorizontally);
        flickable.PointerRelease(10, 20, QtEnums.LeftButton, 0, 0);
        Assert.False(flickable.DraggingVertically);
        Assert.False(flickable.MovingVertically);
        Assert.Equal(["dragging", "dragging", "draggingVertically", "draggingVertically", "moving", "moving", "movingVertically", "movingVertically"], seen.Order());

        seen.Clear();
        flickable.Wheel(10, 10, 0, -120, 0, 0, 0, 0, false);
        Assert.True(flickable.MovingVertically);
        loop.Advance(TimeSpan.FromSeconds(2));
        Assert.False(flickable.MovingVertically);
        Assert.Equal(["moving", "moving", "movingVertically", "movingVertically"], seen.Order());
    }

    private static (Flickable Flickable, ManualEventLoop Loop, QmlEngine Engine) Make()
    {
        var loop = new ManualEventLoop();
        var engine = new QmlEngine(new ModuleRegistry(), loop);
        var flickable = new Flickable { Engine = engine, Width = 100, Height = 100, ContentHeight = 1000 };
        flickable.MarkComplete();
        return (flickable, loop, engine);
    }

    private sealed class Recorder(List<string> seen) : IPropertyObserver
    {
        public void OnPropertyChanged(QObject sender, string property) => seen.Add(property);
    }
}
