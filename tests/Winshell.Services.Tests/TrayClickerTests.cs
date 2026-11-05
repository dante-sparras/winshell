using Winshell.Native;
using Winshell.Tray;

namespace Winshell.Services.Tests;

public sealed class TrayClickerTests
{
    [Fact]
    public void An_elevated_shell_clicks_no_app_above_medium_integrity()
    {
        const uint low = 0x1000, medium = ProcessIntegrity.Medium, high = 0x3000, system = 0x4000;
        Assert.False(TrayClicker.MayClick(high, high));
        Assert.False(TrayClicker.MayClick(high, system));
        Assert.False(TrayClicker.MayClick(high, null));
        Assert.True(TrayClicker.MayClick(high, medium));
        Assert.True(TrayClicker.MayClick(high, low));
        // A shell that cannot read its own level is treated as elevated.
        Assert.False(TrayClicker.MayClick(null, high));
        Assert.True(TrayClicker.MayClick(null, medium));
        // Unelevated, Windows keeps the shell out of elevated apps itself, as it does Explorer.
        Assert.True(TrayClicker.MayClick(medium, high));
        Assert.True(TrayClicker.MayClick(medium, medium));
    }

    [Theory]
    [InlineData(ProcessIntegrity.Medium, true)]
    [InlineData(0x3000u, false)]
    public void A_click_hands_over_the_foreground_then_reaches_the_app_only_when_this_process_may_click_it(uint shell,
        bool clicks)
    {
        // The app is elevated, and so is the shell in the second case.
        const nint app = 0x100;
        var backend = new FakeTrayBackend
            { Windows = { [app] = 42 }, Integrities = { [42] = 0x3000, [(uint)Environment.ProcessId] = shell } };
        using var clicker = new TrayClicker(new ManualEventLoop(), backend);

        clicker.Click(new TrayIcon { Key = "app", Window = app, Id = 1, CallbackMessage = 0x8001 }, TrayClick.Left,
            null);

        // An app before version 4 is told the mouse message: WM_LBUTTONDOWN, then WM_LBUTTONUP.
        string[] calls = clicks ? ["foreground 100 42", "notify", "notify"] : [];
        (nint, uint, nint, nint)[] sent = clicks ? [(app, 0x8001, 1, 0x201), (app, 0x8001, 1, 0x202)] : [];
        Assert.Equal(calls, backend.Calls);
        Assert.Equal(sent, backend.Notified);
    }
}
