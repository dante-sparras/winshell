using System.ComponentModel;
using Winshell.Power;

namespace Winshell.Services.Tests;

public sealed class PowerServiceTests
{
    private static PowerDeviceInfo Battery(string id, double percentage = 0.5) => new(id, "Model", PowerDeviceType.Battery, true, true,
        PowerDeviceState.Discharging, percentage, 10, 20, -5, 3600, 0, 90, "battery");

    private static PowerSnapshot Snapshot(params PowerDeviceInfo[] devices) =>
        new(true, devices.FirstOrDefault() ?? PowerDeviceInfo.NoBattery, devices, devices.Length > 0);

    private static BrightnessInfo Monitor(int current) => new("d", "Monitor", BrightnessConnection.External, 0, 100, current);

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void A_snapshot_changes_only_the_properties_that_differ()
    {
        var loop = new ManualEventLoop();
        var source = new FakeSource<PowerSnapshot>(Snapshot(Battery("a", 0.5)));
        using var power = new PowerService(loop, source);
        var changes = Watch(power.DisplayDevice);

        source.Push(Snapshot(Battery("a", 0.25)));
        Assert.Empty(changes);
        loop.Process();

        Assert.Equal([nameof(PowerDevice.Percentage)], changes);
        Assert.Equal(0.25, power.DisplayDevice.Percentage);
    }

    [Fact]
    public void A_device_that_stays_is_the_same_object()
    {
        var loop = new ManualEventLoop();
        var source = new FakeSource<PowerSnapshot>(Snapshot(Battery("a"), Battery("b")));
        using var power = new PowerService(loop, source);
        var kept = power.Devices[1];

        source.Push(Snapshot(Battery("b", 0.75)));
        loop.Process();

        var only = Assert.Single(power.Devices);
        Assert.Same(kept, only);
        Assert.Equal(0.75, only.Percentage);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_source()
    {
        var loop = new ManualEventLoop();
        var source = new FakeSource<PowerSnapshot>(Snapshot());
        var power = new PowerService(loop, source);

        source.Push(Snapshot(Battery("a")));
        power.Dispose();
        loop.Process();

        Assert.Empty(power.Devices);
    }

    [Fact]
    public void A_power_mode_Windows_refuses_is_logged()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (_, _, message) => lines.Add(message) };
        using var mode = new PowerModeService(new ManualEventLoop(), log, new FakeSource<PowerModeSnapshot>(PowerModeSnapshot.Initial), _ => 5);

        mode.Mode = PowerModeLevel.BestPerformance;

        Assert.Contains(lines, l => l.Contains("error 5", StringComparison.Ordinal));
    }

    [Fact]
    public void A_power_mode_that_is_not_a_level_is_refused()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (_, _, message) => lines.Add(message) };
        var asked = 0;
        using var mode = new PowerModeService(new ManualEventLoop(), log, new FakeSource<PowerModeSnapshot>(PowerModeSnapshot.Initial), _ =>
        {
            asked++;
            return 0;
        });

        mode.Mode = (PowerModeLevel)9;

        Assert.Equal(0, asked);
        Assert.Single(lines);
    }

    [Fact]
    public async Task A_brightness_shows_at_once_and_a_later_read_replaces_it()
    {
        var loop = new ManualEventLoop();
        var written = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new BrightnessWriter((_, value) =>
        {
            written.TrySetResult(value);
            return 0;
        });
        var source = new FakeSource<BrightnessSnapshot>(new BrightnessSnapshot(true, [Monitor(10)]));
        using var brightness = new BrightnessService(loop, Log.Default, source, writer);
        var monitor = brightness.Monitors[0];

        monitor.Brightness = 150;

        Assert.Equal(100, monitor.Brightness);
        Assert.Equal(100, await written.Task.WaitAsync(TestContext.Current.CancellationToken));
        await Wait.UntilAsync(() => !writer.IsWriting("d"), "the write to end");
        source.Push(new BrightnessSnapshot(true, [Monitor(60)]));
        loop.Process();
        Assert.Equal(60, monitor.Brightness);
    }

    [Fact]
    public void An_idle_monitor_turns_on_after_the_timeout_and_a_keep_awake_holds_it_off()
    {
        var loop = new ManualEventLoop();
        var idle = 0.0;
        var held = false;
        using var monitor = new IdleMonitor(loop, Log.Default, () => idle, () => held) { Timeout = 10 };
        Assert.False(monitor.IsIdle);

        idle = 11;
        held = true;
        loop.Advance(TimeSpan.FromSeconds(5));
        Assert.False(monitor.IsIdle);

        held = false;
        loop.Advance(TimeSpan.FromSeconds(5));
        Assert.True(monitor.IsIdle);
    }

    [Fact]
    public void An_idle_monitor_that_cannot_be_idle_does_not_poll()
    {
        var loop = new ManualEventLoop();
        using var monitor = new IdleMonitor(loop, Log.Default, () => 0, () => false) { Timeout = 10 };

        monitor.Enabled = false;

        Assert.Null(loop.NextDue);
    }

    [Theory]
    [InlineData(false, 100, 5)]
    [InlineData(false, 2, 2)]
    [InlineData(false, 0.1, 0.25)]
    [InlineData(true, 100, 1)]
    [InlineData(false, -1, 1)]
    public void An_idle_monitor_looks_again_when_the_timeout_can_pass(bool idle, double remaining, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), IdleMonitor.NextCheck(idle, remaining));

    [Fact]
    public void Keep_awake_lets_go_when_disposed()
    {
        var awake = new KeepAwake { Enabled = true };
        Assert.True(KeepAwake.AnyActive);
        Assert.True(awake.HoldsRequest);

        awake.Dispose();

        Assert.False(awake.Enabled);
        Assert.False(awake.HoldsRequest);
    }
}
