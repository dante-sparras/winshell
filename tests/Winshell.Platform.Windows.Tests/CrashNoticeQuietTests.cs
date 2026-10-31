using Winshell.Platform.Windows.Tests.Snapshots;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A crash notice waits out a presentation, a full-screen app and quiet hours, as a Windows toast does.</summary>
[Collection(ThemeCollection.Name)]
public sealed class CrashNoticeQuietTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualEventLoop _loop = new();
    private readonly SnapshotPlatform _platform = new(1);
    private readonly List<string> _messages = [];
    private ShellHost? _host;
    private ShellNotices? _notices;

    private static ModuleRegistry Modules()
    {
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        return registry;
    }

    private ShellNotices Start(bool quiet)
    {
        Directory.CreateDirectory(_dir);
        var root = Path.Combine(_dir, "shell.qml");
        File.WriteAllText(root, "import Winshell\nShellRoot { }\n");
        var log = new Log { Sink = (_, category, message) => _messages.Add($"{category}: {message}") };
        _host = new ShellHost(root, _loop, _platform, log, Modules)
        {
            WatchFiles = false,
            Crashes = new CrashReports(Path.Combine(_dir, "crashes")),
        };
        _notices = new ShellNotices(_host, _loop, _platform, log, _ => { }, Modules) { IsQuiet = () => quiet };
        _host.Start();
        _loop.Process();
        return _notices;
    }

    [Fact]
    public void A_crash_while_windows_is_quiet_is_logged_and_shows_no_notice()
    {
        var notices = Start(quiet: true);

        _host!.Crashes!.Write("test", new InvalidOperationException("broke"), subject: "a window", recovery: "It opens again.");
        _loop.Process();

        Assert.Null(notices.Current);
        Assert.Contains(_messages, m => m.Contains("held back", StringComparison.Ordinal));
    }

    [Fact]
    public void A_crash_shows_its_notice_otherwise()
    {
        var notices = Start(quiet: false);

        _host!.Crashes!.Write("test", new InvalidOperationException("broke"), subject: "a window", recovery: "It opens again.");
        _loop.Process();

        Assert.Equal(NoticeKind.Crash, notices.Current?.Kind);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void Windows_holds_a_toast_back_in_these_states(int state, bool quiet) =>
        Assert.Equal(quiet, ShellNotices.QuietState(state));

    public void Dispose()
    {
        _notices?.Dispose();
        _host?.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
