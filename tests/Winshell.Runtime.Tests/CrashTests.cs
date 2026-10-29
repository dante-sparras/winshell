using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// Crash reports, and a window whose native window fails: it is reported, closed, and opened again after a backoff,
/// while the rest of the shell keeps running. Every report goes to a temp folder this class creates.
/// </summary>
public sealed class CrashTests : IDisposable
{
    private const string Panel = """
        import Winshell
        import QtQuick
        ShellRoot {
          PanelWindow {
            objectName: "bar"
            anchors.bottom: true
            implicitHeight: 48
          }
          PanelWindow {
            objectName: "other"
            anchors.top: true
            implicitHeight: 32
          }
        }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "winshell-crash-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [Fact]
    public void A_report_says_where_what_and_the_end_of_the_log()
    {
        Directory.CreateDirectory(_folder);
        var log = Path.Combine(_folder, "log.txt");
        File.WriteAllLines(log, Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var crashes = new CrashReports(Path.Combine(_folder, "crashes")) { ShellId = "my-shell", ConfigPath = @"C:\config\shell.qml", LogPath = log };
        CrashReport? raised = null;
        crashes.Written += r => raised = r;

        var report = crashes.Write("PanelWindow in Taskbar.qml", Thrown("the paint failed"));

        Assert.NotNull(report);
        Assert.Same(report, raised);
        Assert.False(report.Fatal);
        Assert.StartsWith(Path.Combine(_folder, "crashes"), report.Path, StringComparison.Ordinal);
        Assert.EndsWith("-panelwindow-in-taskbar-qml.txt", report.Path, StringComparison.Ordinal);
        var text = File.ReadAllText(report.Path);
        Assert.StartsWith("Winshell crash report", text, StringComparison.Ordinal);
        Assert.Contains("This file stays on this PC.", text, StringComparison.Ordinal);
        Assert.Contains("Winshell: " + ProductVersion.Current.Text, text, StringComparison.Ordinal);
        Assert.Contains("Windows: ", text, StringComparison.Ordinal);
        Assert.Contains("Shell id: my-shell", text, StringComparison.Ordinal);
        Assert.Contains(@"Config: C:\config\shell.qml", text, StringComparison.Ordinal);
        Assert.Contains("Where: PanelWindow in Taskbar.qml", text, StringComparison.Ordinal);
        Assert.Contains("Winshell closed: no", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: the paint failed", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), text, StringComparison.Ordinal);
        Assert.Contains("last 40 lines", text, StringComparison.Ordinal);
        Assert.Contains("line 100", text, StringComparison.Ordinal);
        Assert.Contains("line 61", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line 60" + Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_failure_from_the_same_place_is_reported_once()
    {
        var crashes = new CrashReports(_folder);
        Exception Same() => Thrown("again");

        Assert.NotNull(crashes.WriteOnce("event loop", Same()));
        Assert.Null(crashes.WriteOnce("event loop", Same()));
        Assert.NotNull(crashes.WriteOnce("background task", Same()));
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public void The_folder_keeps_the_newest_reports()
    {
        var crashes = new CrashReports(_folder) { Keep = 3 };
        var paths = Enumerable.Range(1, 5).Select(i => crashes.Write($"report {i}", Thrown("x"))!.Path).ToList();

        var kept = Directory.GetFiles(_folder).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(paths.TakeLast(3).Order(StringComparer.Ordinal), kept);
    }

    [Fact]
    public void A_folder_that_cannot_be_written_writes_nothing_and_does_not_throw()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(file, "");

        Assert.Null(new CrashReports(file).Write("here", Thrown("x")));
    }

    [Fact]
    public void A_failed_window_opens_again_after_1_2_4_8_and_16_seconds_then_stays_closed()
    {
        var restarts = new SurfaceRestarts();
        var now = TimeSpan.FromMinutes(1);
        var delays = new List<TimeSpan?>();
        for (var i = 0; i < 6; i++)
        {
            delays.Add(restarts.Fail(now));
            now += TimeSpan.FromSeconds(20);
        }

        Assert.Equal([1, 2, 4, 8, 16], delays.Take(5).Select(d => d!.Value.TotalSeconds));
        Assert.Null(delays[5]);
    }

    [Fact]
    public void Failures_older_than_five_minutes_no_longer_count()
    {
        var restarts = new SurfaceRestarts();
        for (var i = 0; i < 5; i++)
            restarts.Fail(TimeSpan.FromSeconds(i));

        Assert.Equal(TimeSpan.FromSeconds(1), restarts.Fail(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void A_window_that_fails_is_reported_closed_and_opened_again_while_the_others_keep_running()
    {
        using var files = new TestShell(("shell.qml", Panel));
        var platform = new FakePlatform();
        var crashes = new CrashReports(_folder);
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log) { WatchFiles = false, Crashes = crashes };
        Assert.True(host.Start());
        files.Loop.Process();
        var bar = platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "bar");
        var other = platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "other");
        var window = (ShellWindow)bar.Client;

        bar.Client.OnFailed(Thrown("the surface broke"));

        Assert.True(bar.Disposed);
        Assert.False(window.BackingWindowVisible);
        Assert.False(other.Disposed);
        var report = Assert.Single(Directory.GetFiles(_folder));
        Assert.Contains("Where: PanelWindow \"bar\" in shell.qml on DISPLAY1", File.ReadAllText(report), StringComparison.Ordinal);
        Assert.Contains("Error window: PanelWindow \"bar\" in shell.qml on DISPLAY1 failed and opens again in 1 s: InvalidOperationException: the surface broke", files.Messages);
        Assert.Contains($"Info window: Wrote a crash report to \"{report}\"", files.Messages);

        files.Loop.Advance(TimeSpan.FromMilliseconds(900));
        Assert.DoesNotContain(platform.Windows, w => w.Client == window);

        files.Loop.Advance(TimeSpan.FromMilliseconds(200));
        var again = Assert.Single(platform.Windows, w => w.Client == window);
        Assert.NotSame(bar, again);
        Assert.True(again.IsVisible);
        Assert.Equal(48, again.Panel?.Height);
        Assert.True(window.BackingWindowVisible);
    }

    [Fact]
    public void A_window_that_keeps_failing_stays_closed_until_the_configuration_reloads()
    {
        using var files = new TestShell(("shell.qml", Panel));
        var platform = new FakePlatform();
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log) { WatchFiles = false };
        Assert.True(host.Start());
        files.Loop.Process();
        ShellWindow Bar() => (ShellWindow)host.Root!.OwnedObjects.OfType<ShellWindow>().Single(w => w.ObjectName == "bar");
        var window = Bar();

        for (var i = 0; i < 6; i++)
        {
            var backend = Assert.Single(platform.Windows, w => w.Client == window);
            backend.Client.OnFailed(Thrown($"failure {i}"));
            files.Loop.Advance(TimeSpan.FromSeconds(20));
        }

        Assert.DoesNotContain(platform.Windows, w => w.Client == window);
        Assert.Contains("Error window: PanelWindow \"bar\" in shell.qml on DISPLAY1 failed 6 times in 5 minutes and stays closed until the configuration reloads: InvalidOperationException: failure 5", files.Messages);
        files.Loop.Advance(TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(platform.Windows, w => w.Client == window);

        host.Reload(hard: false);
        files.Loop.Process();
        Assert.Single(platform.Windows, w => w.Client == Bar());
    }

    [Fact]
    public void Work_that_throws_on_the_loop_is_logged_and_raised_and_the_loop_goes_on()
    {
        var loop = new ManualEventLoop { Log = new Log { Sink = (_, _, _) => { } } };
        var failed = new List<Exception>();
        loop.Failed += failed.Add;
        var ran = false;

        loop.Post(() => throw new InvalidOperationException("posted"));
        loop.Post(() => ran = true);
        loop.Process();

        Assert.Equal("posted", Assert.Single(failed).Message);
        Assert.True(ran);
    }
}
