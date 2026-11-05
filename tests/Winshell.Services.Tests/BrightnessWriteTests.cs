using Winshell.Power;

namespace Winshell.Services.Tests;

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
            {
                written.Add(value);
            }

            started.Release();
            release.Wait(TestContext.Current.CancellationToken);
            return 0;
        });
        var monitor = new BrightnessMonitor(writer, Log.Default);
        monitor.Update(new BrightnessInfo("DISPLAY1", "Panel", BrightnessConnection.Internal, 0, 100, 40));

        // The write is held until release is set below, so a setter that waited for it would never return.
        monitor.Brightness = 50;
        Assert.Equal(50, monitor.Brightness);

        started.Wait(TestContext.Current.CancellationToken);
        for (var value = 51; value <= 80; value++)
            monitor.Brightness = value;
        Assert.Equal(80, monitor.Brightness);

        // A read that arrives while writes are still running does not move the value back.
        monitor.Update(new BrightnessInfo("DISPLAY1", "Panel", BrightnessConnection.Internal, 0, 100, 40));
        Assert.Equal(80, monitor.Brightness);

        release.Set();
        Wait.Until(() => !writer.IsWriting("DISPLAY1"), "The writes did not finish.");
        lock (written)
        {
            Assert.Equal([50, 80], written);
        }

        // After the writes, the next read is what the property shows.
        monitor.Update(new BrightnessInfo("DISPLAY1", "Panel", BrightnessConnection.Internal, 0, 100, 78));
        Assert.Equal(78, monitor.Brightness);
    }
}
