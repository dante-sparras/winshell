using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Physical size becomes millimeters, and a monitor with no size stays at 0.</summary>
public class MonitorSizeTests(ITestOutputHelper output)
{
    [Fact]
    public void Inches_become_millimeters_and_a_missing_side_is_no_size()
    {
        Assert.Equal((254d, 127d), MonitorSize.FromInches(10, 5));
        Assert.Equal((0d, 0d), MonitorSize.FromInches(0, 10));
        Assert.Equal((0d, 0d), MonitorSize.FromInches(-1, 10));
        Assert.Equal((0d, 0d), MonitorSize.FromInches(null, 10));
        Assert.Equal((0d, 0d), MonitorSize.Read(""));
    }

    [Fact]
    public void Connected_monitors_report_a_physical_size_or_zero()
    {
        foreach (var monitor in Screens.Enumerate())
        {
            var info = monitor.Info;
            output.WriteLine($"{info.Name} {info.Model} {info.Geometry.Width}x{info.Geometry.Height} dpr {info.DevicePixelRatio} mm {info.PhysicalWidthMm}x{info.PhysicalHeightMm}");
            if (info.PhysicalWidthMm == 0)
                Assert.Equal(0, info.PhysicalHeightMm);
            else
                Assert.True(info.PhysicalHeightMm > 0);
            var screen = new Winshell.Runtime.Shell.ShellScreen(info);
            if (info.PhysicalWidthMm == 0)
            {
                Assert.Equal(0, screen.PhysicalPixelDensity);
                Assert.Equal(0, screen.LogicalPixelDensity);
            }
            else
            {
                Assert.True(screen.PhysicalPixelDensity > 0);
                Assert.True(screen.LogicalPixelDensity > 0);
            }
        }
    }

    [Fact]
    public void A_monitor_is_asked_once_and_only_the_first_read_waits_for_it()
    {
        var answer = new TaskCompletionSource<(double, double)>();
        var queries = 0;
        var cache = new MonitorSizeCache(_ =>
        {
            queries++;
            return answer.Task;
        }, TimeSpan.FromMilliseconds(1));

        Assert.Equal((0d, 0d), cache.Read("monitor"));
        Assert.Equal((0d, 0d), cache.Read("monitor"));
        answer.SetResult((697, 393));
        Assert.Equal((697d, 393d), cache.Read("monitor"));
        Assert.Equal(1, queries);

        Assert.Equal((0d, 0d), cache.Read(""));
        Assert.Equal(1, queries);
    }
}
