using System.Collections.ObjectModel;
using Winshell.Power;

namespace Winshell.Services.Tests;

/// <summary>
/// A monitor stays when one read of it fails, and a change to the screens or a burst of writes asks for one new read. A fake write
/// stands in for Windows, so the PC's brightness never changes.
/// </summary>
public class BrightnessRefreshTests
{
    [Fact]
    public void A_failed_read_keeps_the_last_reading_of_a_monitor()
    {
        var last = new BrightnessInfo(@"\.\DISPLAY2", "Desk", BrightnessConnection.External, 0, 100, 40);

        var kept = BrightnessBackend.External(@"\.\DISPLAY2", "Desk", null, [last]);

        Assert.Equal(last, kept);
    }

    [Fact]
    public void A_failed_read_of_a_monitor_never_read_gives_nothing_and_a_good_read_replaces_the_old_one()
    {
        var last = new BrightnessInfo(@"\.\DISPLAY2", "Desk", BrightnessConnection.External, 0, 100, 40);

        Assert.Null(BrightnessBackend.External(@"\.\DISPLAY3", "Other", null, [last]));
        Assert.Equal(
            new BrightnessInfo(@"\.\DISPLAY2", "Desk", BrightnessConnection.External, 0, 100, 70),
            BrightnessBackend.External(@"\.\DISPLAY2", "Desk", (0, 70, 100), [last]));
    }

    [Fact]
    public void A_change_to_the_shells_screens_reads_brightness_again()
    {
        var loop = new ManualEventLoop();
        var screens = new ObservableCollection<ScreenInfo>();
        var source = new FakeBrightnessBackend(
            new BrightnessSnapshot(true,
                [new BrightnessInfo("d", "Monitor", BrightnessConnection.External, 0, 100, 10)]),
            (_, _) => null);
        using var brightness = new BrightnessService(loop, Log.Default, screens, source);
        loop.Process();
        Assert.Equal(1, source.Reads);

        screens.Add(new ScreenInfo(@"\.\DISPLAY2", "DISPLAY2", "", "", "", default, default, 1, 0, 0, 60, false));
        loop.Process();

        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public void A_burst_of_writes_asks_for_one_read_after_the_queue_empties()
    {
        var drained = 0;
        var writes = 0;
        using var started = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        var writer = new BrightnessWriter(
            (_, _) =>
            {
                Interlocked.Increment(ref writes);
                started.Release();
                release.Wait(TimeSpan.FromSeconds(10));
                return null;
            },
            Log.Default,
            () => Interlocked.Increment(ref drained));

        writer.Request("DISPLAY1", 10, _ => { });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "The first write did not start.");
        for (var value = 11; value <= 30; value++)
            writer.Request("DISPLAY1", value, _ => { });
        Assert.Equal(0, Volatile.Read(ref drained));

        release.Set();
        Wait.Until(() => Volatile.Read(ref drained) == 1, "The queue did not empty.");

        Assert.Equal(2, Volatile.Read(ref writes));
        Assert.Equal(1, Volatile.Read(ref drained));
    }
}
