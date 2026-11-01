using System.Diagnostics;
using Winshell.Platform.Windows.Services.Power;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Setting a brightness does not wait for the slow write, and a drag writes only the newest value.
/// A fake write stands in for Windows, so the PC's brightness never changes.
/// </summary>
public class BrightnessWriteTests
{
    [Fact]
    public void Setting_brightness_returns_at_once_and_writes_only_the_newest_value()
    {
        var written = new List<int>();
        using var started = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        var writer = new BrightnessWriter((_, value) =>
        {
            lock (written)
                written.Add(value);
            started.Release();
            release.Wait(TimeSpan.FromSeconds(10));
            return 0;
        });
        var monitor = new BrightnessMonitor(writer);
        monitor.Update(new BrightnessInfo("DISPLAY1", "Panel", BrightnessConnection.Internal, 0, 100, 40));

        var clock = Stopwatch.StartNew();
        monitor.Brightness = 50;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Setting the brightness took {clock.Elapsed}.");
        Assert.Equal(50, monitor.Brightness);

        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "The first write did not start.");
        clock.Restart();
        for (var value = 51; value <= 80; value++)
            monitor.Brightness = value;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Setting the brightness took {clock.Elapsed}.");
        Assert.Equal(80, monitor.Brightness);

        // A read that arrives while writes are still running does not move the value back.
        monitor.Update(new BrightnessInfo("DISPLAY1", "Panel", BrightnessConnection.Internal, 0, 100, 40));
        Assert.Equal(80, monitor.Brightness);

        release.Set();
        Wait.Until(() => !writer.IsWriting("DISPLAY1"), "The writes did not finish.");
        lock (written)
            Assert.Equal([50, 80], written);

        // After the writes, the next read is what the property shows.
        monitor.Update(new BrightnessInfo("DISPLAY1", "Panel", BrightnessConnection.Internal, 0, 100, 78));
        Assert.Equal(78, monitor.Brightness);
    }
}
