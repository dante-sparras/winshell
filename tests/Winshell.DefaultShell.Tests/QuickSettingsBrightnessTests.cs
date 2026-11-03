using Winshell.DefaultShell.Taskbar;
using Winshell.Power;

namespace Winshell.DefaultShell.Tests;

/// <summary>The brightness slider against the real brightness service and a fake panel. Nothing here opens a window.</summary>
public sealed class QuickSettingsBrightnessTests
{
    [Fact]
    public async Task The_slider_spans_the_range_the_panel_reports()
    {
        var source = new FakeSource<BrightnessSnapshot>(new(true, [new BrightnessInfo("panel", "Built-in", BrightnessConnection.Internal, 20, 60, 30)]));
        var writer = new BrightnessWriter(static (_, _) => 0);
        using var service = new BrightnessService(new ManualEventLoop(), new Log { MinimumLevel = LogLevel.Fatal }, source, writer);
        var brightness = new QuickSettingsBrightness(service);
        var panel = Assert.Single(service.Monitors);
        Assert.True(brightness.HasPanel);
        Assert.Equal(0.25, brightness.Level, 3);

        brightness.SetLevel(0.5);
        Assert.Equal(40, panel.Brightness);
        Assert.Equal(0.5, brightness.Level, 3);
        await Wait.UntilAsync(() => !writer.IsWriting(panel.DeviceName), "the brightness write");
    }
}
