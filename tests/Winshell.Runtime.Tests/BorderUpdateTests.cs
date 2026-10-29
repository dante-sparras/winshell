using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class BorderUpdateTests
{
    [Fact]
    public void A_border_change_repaints_once_and_an_unchanged_value_does_not_repaint()
    {
        var host = new CountingHost();
        var rectangle = new Rectangle { Width = 10, Height = 10 };
        rectangle.SetHost(host);

        rectangle.Border.Width = 2;
        rectangle.Border.Color = QColor.FromRgb(255, 0, 0);
        Assert.Equal(2, host.Updates);

        rectangle.Border.Width = 2;
        rectangle.Border.Color = QColor.FromRgb(255, 0, 0);
        Assert.Equal(2, host.Updates);
    }

    [Fact]
    public void Setting_a_border_to_its_default_still_repaints_because_the_border_now_shows()
    {
        var host = new CountingHost();
        var rectangle = new Rectangle { Width = 10, Height = 10 };
        rectangle.SetHost(host);

        rectangle.Border.Width = 1;
        Assert.Equal(1, host.Updates);
        Assert.True(rectangle.Border.IsVisible);
    }

    private sealed class CountingHost : IItemHost
    {
        public int Updates;

        public void RequestUpdate() => Updates++;

        public double DevicePixelRatio => 1;

        public QObject? WindowObject => null;

        public void SetFocusItem(Item? item) { }

        public Item? FocusItem => null;
    }
}
