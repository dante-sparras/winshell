using Winshell.Native;
using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>Explorer's desktops, read through the method table for this build of Windows.</summary>
[Trait("Category", "Slow")]
public sealed class VirtualDesktopBackendTests
{
    [Fact]
    public void This_machine_reads_its_desktops_when_the_table_is_known()
    {
        var build = WindowsVersion.Read().Build;
        Assert.True(build > 0);
        var contract = VirtualDesktopBackend.Shared.Contract;
        if (!contract.Supported)
        {
            Assert.Contains($"{build}", contract.Reason, StringComparison.Ordinal);
            Assert.Skip(contract.Reason);
        }

        var read = VirtualDesktopBackend.Shared.TryRead(out var desktops, out var current, out var error);
        // "unavailable:" is a PC without Explorer's desktops; any other failure is a wrong method table.
        Assert.SkipWhen(error.StartsWith("unavailable:", StringComparison.Ordinal), error);
        Assert.True(read, error);

        Assert.NotEmpty(desktops);
        Assert.Contains(desktops, desktop => desktop.Id == current);
        Assert.All(desktops, desktop => Assert.NotEqual(Guid.Empty, desktop.Id));
        var foreground = WindowDesktop.IdOf(Win32.GetForegroundWindow());
        if (foreground.Length > 0)
            Assert.True(Guid.TryParse(foreground, out _));

        using var service = new VirtualDesktopsService(new ManualEventLoop(), new Log(), VirtualDesktopBackend.Shared);
        Assert.True(service.Supported);
        Assert.Equal(desktops.Count, service.Desktops.Count);
        Assert.All(service.Desktops, row => Assert.False(string.IsNullOrWhiteSpace(row.Name)));
    }
}
