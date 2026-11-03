using Winshell.Tray;

namespace Winshell.Services.Tests;

public sealed class TrayClickerTests
{
    [Fact]
    public void An_elevated_shell_clicks_no_app_above_medium_integrity()
    {
        const uint low = 0x1000, high = 0x3000, system = 0x4000, unknown = uint.MaxValue;
        Assert.False(TrayClicker.MayClick(high, high));
        Assert.False(TrayClicker.MayClick(high, system));
        Assert.False(TrayClicker.MayClick(high, unknown));
        Assert.True(TrayClicker.MayClick(high, TrayClicker.MediumIntegrity));
        Assert.True(TrayClicker.MayClick(high, low));
        // Unelevated, Windows keeps the shell out of elevated apps itself, as it does Explorer.
        Assert.True(TrayClicker.MayClick(TrayClicker.MediumIntegrity, high));
        Assert.True(TrayClicker.MayClick(TrayClicker.MediumIntegrity, TrayClicker.MediumIntegrity));

        Assert.InRange(TrayClicker.IntegrityLevel((uint)Environment.ProcessId), low, system);
        // The System Idle Process has no token to read.
        Assert.Equal(unknown, TrayClicker.IntegrityLevel(0));
    }
}
