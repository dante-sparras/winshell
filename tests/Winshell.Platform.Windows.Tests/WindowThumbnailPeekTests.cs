using Winshell.Platform.Windows.Services.WindowList;

namespace Winshell.Platform.Windows.Tests;

/// <summary>What <c>WindowThumbnail.peek</c> asks of DWM. The calls go to a fake, so no real window is hidden.</summary>
public class WindowThumbnailPeekTests
{
    private readonly List<string> _calls = [];
    private bool _turnedOn = true;

    private WindowPeek Fake(bool succeeds = true) => new(() => _turnedOn, (on, window, _) =>
    {
        _calls.Add((on ? "on " : "off ") + window);
        return succeeds;
    });

    private static AppWindow Window(nint hwnd) => new(null!, hwnd);

    [Fact]
    public void Peek_shows_the_window_until_it_is_turned_off()
    {
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Window = Window(7) };

        thumbnail.Peek = true;
        thumbnail.Peek = false;

        Assert.Equal(["on 7", "off 7"], _calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void Peek_needs_a_window()
    {
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Peek = true };
        Assert.Empty(_calls);

        thumbnail.Window = Window(7);

        Assert.Equal(["on 7"], _calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void Peek_stays_off_when_the_user_turned_it_off()
    {
        _turnedOn = false;
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Window = Window(7) };

        thumbnail.Peek = true;
        thumbnail.Peek = false;

        Assert.Empty(_calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void A_setting_turned_on_later_counts_at_the_next_peek()
    {
        _turnedOn = false;
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Window = Window(7) };
        thumbnail.Peek = true;
        thumbnail.Peek = false;

        _turnedOn = true;
        thumbnail.Peek = true;

        Assert.Equal(["on 7"], _calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void A_peek_Windows_refused_is_not_ended()
    {
        var thumbnail = new WindowThumbnail { Peeker = Fake(succeeds: false), Window = Window(7) };

        thumbnail.Peek = true;
        thumbnail.Peek = false;

        Assert.Equal(["on 7"], _calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void Changing_the_window_moves_the_peek()
    {
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Window = Window(7), Peek = true };

        thumbnail.Window = Window(8);

        Assert.Equal(["on 7", "off 7", "on 8"], _calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void A_window_that_closes_ends_the_peek()
    {
        var window = Window(7);
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Window = window, Peek = true };

        window.Emit("closed");

        Assert.Equal(["on 7", "off 7"], _calls);
        thumbnail.Destroy();
    }

    [Fact]
    public void A_destroyed_thumbnail_ends_the_peek()
    {
        var thumbnail = new WindowThumbnail { Peeker = Fake(), Window = Window(7), Peek = true };

        thumbnail.Destroy();

        Assert.Equal(["on 7", "off 7"], _calls);
    }
}
