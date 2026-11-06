using Winshell.Power;

namespace Winshell.Services.Tests;

public sealed class PowerModeBackendTests
{
    [Fact]
    public void Power_mode_guids_round_trip_and_the_deprecated_performance_guid_is_only_read()
    {
        Assert.Equal(PowerModeLevel.BestEfficiency, PowerModeBackend.FromGuid(PowerModeBackend.BestEfficiency));
        Assert.Equal(PowerModeLevel.Balanced, PowerModeBackend.FromGuid(Guid.Empty));
        Assert.Equal(PowerModeLevel.BestPerformance, PowerModeBackend.FromGuid(PowerModeBackend.BestPerformance));
        Assert.Equal(PowerModeLevel.BestPerformance, PowerModeBackend.FromGuid(PowerModeBackend.DeprecatedPerformance));
        Assert.Equal(PowerModeBackend.BestEfficiency, PowerModeBackend.ToGuid(PowerModeLevel.BestEfficiency));
        Assert.Equal(Guid.Empty, PowerModeBackend.ToGuid(PowerModeLevel.Balanced));
        Assert.Equal(PowerModeBackend.BestPerformance, PowerModeBackend.ToGuid(PowerModeLevel.BestPerformance));
        Assert.NotEqual(PowerModeBackend.DeprecatedPerformance,
            PowerModeBackend.ToGuid(PowerModeLevel.BestPerformance));

        Assert.Equal(0, (int)PowerModeLevel.BestEfficiency);
        Assert.Equal(1, (int)PowerModeLevel.Balanced);
        Assert.Equal(2, (int)PowerModeLevel.BestPerformance);
        Assert.Equal(0, (int)EffectivePowerMode.EnergySaver);
        Assert.Equal(2, (int)EffectivePowerMode.Balanced);
        Assert.Equal(0, (int)BrightnessConnection.Internal);
        Assert.Equal(1, (int)BrightnessConnection.External);
        using var monitor = new IdleMonitor(new ManualEventLoop(), new Log(), () => 0, () => false);
        Assert.True(monitor.RespectKeepAwake);
    }
}
