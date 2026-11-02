using System.Globalization;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskbarClockModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-clock-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(0, true)]
    [InlineData(120, true)]
    [InlineData(499, true)]
    [InlineData(500, false)]
    [InlineData(5000, false)]
    public void A_click_just_after_the_popup_was_dismissed_is_the_click_that_dismissed_it(int after, bool same)
    {
        var dismissed = new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);

        Assert.Equal(same, TaskbarClockModel.ClosedBySameClick(dismissed, dismissed.AddMilliseconds(after)));
    }

    [Fact]
    public void A_popup_that_was_never_dismissed_lets_the_first_click_open_it() =>
        Assert.False(TaskbarClockModel.ClosedBySameClick(default, new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void The_clock_shows_the_time_and_the_date_and_seconds_when_they_are_on()
    {
        using var preferences = new Preferences(Path.Combine(_folder, "preferences.toml"), new ManualEventLoop());
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 15, 4, 5, TimeSpan.Zero));
        var culture = CultureInfo.GetCultureInfo("en-US");
        var local = time.GetLocalNow().LocalDateTime;
        var model = new TaskbarClockModel(time, preferences, culture);

        model.Refresh();

        Assert.NotEqual(local.ToString("t", culture), local.ToString("T", culture));
        Assert.Equal(local.ToString("t", culture) + "\n" + local.ToString("d", culture), model.Text);
        Assert.Equal(local.ToString("t", culture) + ", " + local.ToString("D", culture), model.AccessibleName);

        preferences.ClockSeconds = true;
        model.Refresh();

        Assert.Equal(local.ToString("T", culture) + "\n" + local.ToString("d", culture), model.Text);
        Assert.Equal(local.ToString("t", culture) + ", " + local.ToString("D", culture), model.AccessibleName);
    }

    [Theory]
    [InlineData(5, 0, true, 1000)]
    [InlineData(5, 980, true, 1020)]
    [InlineData(0, 0, false, 60000)]
    [InlineData(59, 980, false, 60020)]
    public void The_next_tick_waits_until_the_clock_actually_changes(int second, int millisecond, bool seconds, int waitMs)
    {
        var now = new DateTimeOffset(2026, 11, 2, 15, 4, second, millisecond, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMilliseconds(waitMs), TaskbarClockModel.UntilNext(now, seconds));
    }
}
