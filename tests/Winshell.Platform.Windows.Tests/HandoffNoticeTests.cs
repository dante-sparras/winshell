using Winshell.Platform.Windows.Tests.Snapshots;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A throw while handing a new generation over is a failed reload, and the config notice says so.</summary>
[Collection(ThemeCollection.Name)]
public sealed class HandoffNoticeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualEventLoop _loop = new();
    private readonly SnapshotPlatform _platform = new(1);

    public HandoffNoticeTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void A_reload_that_throws_while_handing_objects_over_shows_the_config_notice()
    {
        var root = Path.Combine(_dir, "shell.qml");
        File.WriteAllText(root, """
            import Winshell
            import Broken
            ShellRoot {
              objectName: "good"
              ThrowOnHandoff {}
            }
            """);
        var log = new Log();
        using var host = new ShellHost(root, _loop, _platform, log, Modules) { WatchFiles = false };
        using var notices = new ShellNotices(host, _loop, _platform, log, _ => { }, Modules);
        Assert.True(host.Start());
        _loop.Process();
        var running = host.Root!;
        Assert.Null(notices.Current);

        host.Reload(hard: false);
        _loop.Process();

        Assert.Same(running, host.Root);
        Assert.False(running.IsDestroyed);
        Assert.Equal(NoticeKind.ConfigError, notices.Current?.Kind);
        Assert.Equal("Winshell keeps running the last version that loaded.", notices.Current?.Note);
        Assert.Contains("handoff failed", notices.Current?.Body, StringComparison.Ordinal);
    }

    private static ModuleRegistry Modules()
    {
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        registry.Module("Broken").Type<ThrowOnHandoff>("ThrowOnHandoff");
        return registry;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class ThrowOnHandoff : Reloadable
    {
        protected override void OnReload(QObject? oldInstance)
        {
            if (oldInstance is not null)
                throw new InvalidOperationException("handoff failed");
        }
    }
}
