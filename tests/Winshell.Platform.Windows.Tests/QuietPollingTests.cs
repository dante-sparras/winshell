using Winshell.Platform.Windows.Services.Power;
using Winshell.Platform.Windows.Services.Tray;
using Winshell.Platform.Windows.Services.Winland;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Polls that run all day wake the CPU no more often than their answer needs.</summary>
public sealed class QuietPollingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private QmlEngine? _engine;

    [Fact]
    public void An_active_tray_has_no_half_second_poll()
    {
        var loop = new ManualEventLoop();
        var id = Guid.NewGuid().ToString("N");
        using var host = new TrayHost(loop, new Log(), new TrayNames("WinshellQuietTray-" + id, "WinshellAbsentExplorer-" + id, "Local\\winshell-quiet-tray-" + id), () => { });
        host.Start();

        Assert.Equal(TimeSpan.FromSeconds(30), loop.NextDue - loop.Now);
    }

    [Fact]
    public void An_idle_monitor_looks_again_when_the_timeout_can_pass_and_once_a_second_after()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), IdleMonitor.NextCheck(idle: false, remaining: 2));
        Assert.Equal(TimeSpan.FromSeconds(5), IdleMonitor.NextCheck(idle: false, remaining: 300));
        Assert.Equal(TimeSpan.FromSeconds(0.25), IdleMonitor.NextCheck(idle: false, remaining: 0.01));
        Assert.Equal(TimeSpan.FromSeconds(1), IdleMonitor.NextCheck(idle: true, remaining: -60));
        // Held awake by KeepAwake past the timeout.
        Assert.Equal(TimeSpan.FromSeconds(1), IdleMonitor.NextCheck(idle: false, remaining: -60));
    }

    [Fact]
    public void An_idle_monitor_that_cannot_be_idle_does_not_poll()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "shell.qml"), """
            import QtQuick
            import Winshell.Power
            Item {
              property var idle: monitor
              IdleMonitor { id: monitor }
            }
            """);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var loop = new ManualEventLoop();
        _engine = new QmlEngine(registry, loop, new Log(), _directory);
        var root = _engine.CreateRoot(Path.Combine(_directory, "shell.qml"));
        loop.Process();
        var monitor = (QObject)root.ReadProperty("idle")!;

        Assert.Null(loop.NextDue);
        monitor.WriteProperty("timeout", 300.0);
        Assert.NotNull(loop.NextDue);
        monitor.WriteProperty("enabled", false);
        Assert.Null(loop.NextDue);
        Assert.False((bool)monitor.ReadProperty("isIdle")!);
    }

    [Fact]
    public void Looking_for_winland_backs_off_from_3_to_30_seconds()
    {
        var poll = TimeSpan.FromSeconds(3);
        List<double> waits = [];
        for (var i = 0; i < 6; i++)
        {
            waits.Add(poll.TotalSeconds);
            poll = WinlandConnection.NextPoll(poll);
        }
        Assert.Equal([3, 6, 12, 24, 30, 30], waits);
    }

    public void Dispose()
    {
        _engine?.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
