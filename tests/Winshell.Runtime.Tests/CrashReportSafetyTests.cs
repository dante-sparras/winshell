using System.Diagnostics;
using System.Runtime.CompilerServices;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// What a crash report must not do: keep a secret, hide a different crash, lose a report to another writer, keep
/// the wrong ones, or delete a file that is not a report. And windows that fail while they are first made or placed.
/// Every file is under a temp folder this class creates.
/// </summary>
public sealed class CrashReportSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-crash-tests", Guid.NewGuid().ToString("N"));

    public CrashReportSafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // A junction is removed as a link, so what it points at is deleted once, through its own path.
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            if (new DirectoryInfo(dir).LinkTarget is not null)
                Directory.Delete(dir);
        }
        Directory.Delete(_root, true);
    }

    private string Folder(string name = "crashes") => Path.Combine(_root, name);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }
        throw new InvalidOperationException("nothing was thrown");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Helper() => throw new InvalidOperationException("from the helper");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FirstCaller() => Helper();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SecondCaller() => Helper();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FirstTask() => throw new InvalidOperationException("first task");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SecondTask() => throw new InvalidOperationException("second task");

    // Privacy

    [Fact]
    public void Secrets_in_the_exception_and_its_inner_exceptions_are_taken_out()
    {
        var inner = Catch(() => throw new InvalidOperationException("""request failed: api_key=SENTINEL-INNER-KEY-1 and "client_secret": "SENTINEL-JSON-2" """));
        var outer = new HttpRequestException(
            "GET https://alice:SENTINEL-URLPW-3@example.com/path?token=SENTINEL-QUERY-4&x=1 failed\n" +
            "Authorization: Bearer SENTINEL-BEARER-5\n" +
            "password = 'SENTINEL-PASSWORD-6'; token: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTC03In0.c2lnbmF0dXJl\n" +
            "key ghp_SENTINEL8aaaaaaaaaaaaaaaaaaaaaaa and AbCdEfGh1234567890IjKlMnOp1234567890QrStUv",
            inner);
        var crashes = new CrashReports(Folder());

        var text = File.ReadAllText(crashes.Write("a window", outer)!.Path);

        Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AbCdEfGh1234567890IjKlMnOp", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        // What makes the report useful stays: the URL's host and path, the types and the stack.
        Assert.Contains("https://[redacted]@example.com/path?[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("Authorization: [redacted]", text, StringComparison.Ordinal);
        Assert.Contains("System.Net.Http.HttpRequestException", text, StringComparison.Ordinal);
        Assert.Contains("---> System.InvalidOperationException: request failed", text, StringComparison.Ordinal);
        Assert.Contains($"{nameof(CrashReportSafetyTests)}.{nameof(Catch)}", text, StringComparison.Ordinal);
        Assert.Contains("CrashReportSafetyTests.cs:line ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_in_the_log_are_taken_out_and_what_scripts_printed_is_left_out()
    {
        var log = Path.Combine(_root, "log.txt");
        File.WriteAllLines(log,
        [
            "12:00:00.000  INFO: printed by a script before anything else: SENTINEL-EARLY",
            "  continued: SENTINEL-EARLY-2",
            "12:00:01.000  INFO window: opened",
            "12:00:02.000  WARN scene: shell.qml[3:5]: Error: fetch https://example.com/feed?apikey=SENTINEL-LOG-1 failed",
            "12:00:03.000  INFO: console.log from the config: SENTINEL-CONSOLE-2",
            "    a second line of that message: SENTINEL-CONSOLE-3",
            "12:00:04.000 ERROR network: Proxy-Authorization: Basic SENTINEL-LOG-4",
            "12:00:05.000 DEBUG scene: password=SENTINEL-LOG-5",
            "12:00:06.000  INFO window: closed",
        ]);
        var crashes = new CrashReports(Folder()) { LogPath = log };

        var text = File.ReadAllText(crashes.Write("a window", new InvalidOperationException("x"))!.Path);

        Assert.DoesNotContain("SENTINEL", text, StringComparison.Ordinal);
        Assert.Contains("(4 lines of script output left out)", text, StringComparison.Ordinal);
        Assert.Contains("12:00:01.000  INFO window: opened", text, StringComparison.Ordinal);
        Assert.Contains("fetch https://example.com/feed?[redacted] failed", text, StringComparison.Ordinal);
        Assert.Contains("ERROR network: Proxy-Authorization: [redacted]", text, StringComparison.Ordinal);
        Assert.Contains("12:00:06.000  INFO window: closed", text, StringComparison.Ordinal);
        Assert.Contains("last 5 lines", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_text_is_left_alone()
    {
        const string text = "at Winshell.Runtime.Core.SignalSlot.Emit(Object[] args) in C:\\src\\Signals.cs:line 78\n" +
            "Passed: 5, Author: Dante, shell id 5f4dcc3b5aa765d61d8327deb882cf99, crash-20261029-140312-1234567Z-4321-panelwindow-in-taskbar-qml.txt";

        Assert.Equal(text, CrashRedaction.Redact(text));
    }

    // Deduplication

    [Fact]
    public void Two_different_task_failures_are_two_reports()
    {
        var crashes = new CrashReports(Folder());
        // As TaskScheduler.UnobservedTaskException gives them: aggregates that were never thrown themselves.
        var first = new AggregateException(Catch(FirstTask));
        var second = new AggregateException(Catch(SecondTask));
        Assert.Null(first.StackTrace);

        Assert.NotNull(crashes.WriteOnce("background task", first));
        Assert.NotNull(crashes.WriteOnce("background task", second));
        Assert.Null(crashes.WriteOnce("background task", new AggregateException(Catch(FirstTask))));
        Assert.Equal(2, Directory.GetFiles(Folder()).Length);
    }

    [Fact]
    public void One_helper_failing_for_two_callers_is_two_reports()
    {
        var crashes = new CrashReports(Folder());

        Assert.NotNull(crashes.WriteOnce("event loop", Catch(FirstCaller)));
        Assert.NotNull(crashes.WriteOnce("event loop", Catch(SecondCaller)));
        Assert.Null(crashes.WriteOnce("event loop", Catch(FirstCaller)));
    }

    [Fact]
    public void A_report_that_could_not_be_written_is_written_the_next_time()
    {
        var folder = Folder();
        File.WriteAllText(folder, "a file where the folder should be");
        var crashes = new CrashReports(folder);

        Assert.Null(crashes.WriteOnce("event loop", Catch(FirstCaller)));
        File.Delete(folder);
        Assert.NotNull(crashes.WriteOnce("event loop", Catch(FirstCaller)));
        Assert.Null(crashes.WriteOnce("event loop", Catch(FirstCaller)));
    }

    // Retention and the folder

    [Fact]
    public void Pruning_deletes_only_winshell_reports()
    {
        var folder = Folder();
        Directory.CreateDirectory(folder);
        var others = new[]
        {
            "0000-user-notes.txt",
            "crash-notes.txt",
            // Named like a report, but not one.
            "crash-20000101-000000-0000000Z-1-old.txt",
        };
        foreach (var name in others)
            File.WriteAllText(Path.Combine(folder, name), "not a crash report");
        var crashes = new CrashReports(folder) { Keep = 1 };

        crashes.Write("first", new InvalidOperationException("x"));
        var last = crashes.Write("second", new InvalidOperationException("x"))!.Path;

        var left = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(others.Append(Path.GetFileName(last)).Order(StringComparer.Ordinal), left);
    }

    [Fact]
    public void Nothing_is_deleted_through_a_junction()
    {
        var target = Folder("elsewhere");
        Directory.CreateDirectory(target);
        // Reports a user moved there, or another program's files: none is the crash folder's to delete.
        var crashes = new CrashReports(target);
        var moved = Enumerable.Range(0, 3).Select(i => crashes.Write($"moved {i}", new InvalidOperationException("x"))!.Path).ToList();
        File.WriteAllText(Path.Combine(target, "0000-user-notes.txt"), "keep me");
        var link = Folder();
        Junction(link, target);

        var through = new CrashReports(link) { Keep = 1 };
        Assert.NotNull(through.Write("through the junction", new InvalidOperationException("x")));

        Assert.All(moved, path => Assert.True(File.Exists(path)));
        Assert.True(File.Exists(Path.Combine(target, "0000-user-notes.txt")));
    }

    private static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target })
            psi.ArgumentList.Add(arg);
        using var mklink = Process.Start(psi)!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void Reports_written_in_the_same_instant_are_kept_newest_first()
    {
        var instant = new DateTimeOffset(2031, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var crashes = new CrashReports(Folder()) { Keep = 1, Time = new FakeTimeProvider(instant) };

        for (var i = 0; i < 20; i++)
        {
            var path = crashes.Write("same instant", new InvalidOperationException("x"))!.Path;
            Assert.Equal([path], Directory.GetFiles(Folder()));
        }
    }

    [Fact]
    public void Retention_follows_the_utc_instant_across_a_clock_change()
    {
        // Daylight saving ends: 02:30 at +02:00 is 00:30 UTC; twenty minutes on the wall later it is 02:10 at +01:00, 01:10 UTC.
        var time = new FakeTimeProvider(new DateTimeOffset(2031, 10, 26, 2, 30, 0, TimeSpan.FromHours(2)).ToUniversalTime());
        time.SetLocalTimeZone(ZoneAt(2));
        var crashes = new CrashReports(Folder()) { Keep = 1, Time = time };

        crashes.Write("before", new InvalidOperationException("x"));
        time.SetUtcNow(new DateTimeOffset(2031, 10, 26, 2, 10, 0, TimeSpan.FromHours(1)).ToUniversalTime());
        time.SetLocalTimeZone(ZoneAt(1));
        var later = crashes.Write("after", new InvalidOperationException("x"))!.Path;

        Assert.Equal([later], Directory.GetFiles(Folder()));
        Assert.Contains("Time: 2031-10-26 02:10:00.000 +01:00", File.ReadAllText(later), StringComparison.Ordinal);
    }

    private static TimeZoneInfo ZoneAt(int hours) => TimeZoneInfo.CreateCustomTimeZone($"UTC+{hours}", TimeSpan.FromHours(hours), null, null);

    [Fact]
    public void Two_writers_sharing_the_folder_never_overwrite_each_other()
    {
        // As two instances would: separate writers, each with its own lock, on one folder at one instant.
        var instant = new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var writers = Enumerable.Range(0, 4).Select(_ => new CrashReports(Folder()) { Keep = 1000, Time = new FakeTimeProvider(instant) }).ToList();
        using var start = new Barrier(writers.Count);

        var paths = writers.AsParallel().WithDegreeOfParallelism(writers.Count).SelectMany((writer, w) =>
        {
            start.SignalAndWait();
            return Enumerable.Range(0, 25).Select(i => writer.Write($"writer {w}", new InvalidOperationException($"{w}/{i}"))!.Path).ToList();
        }).ToList();

        Assert.Equal(100, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(100, Directory.GetFiles(Folder()).Length);
        var messages = Directory.GetFiles(Folder()).Select(File.ReadAllText).Select(t => t[t.IndexOf("InvalidOperationException: ", StringComparison.Ordinal)..].Split('\n')[0].Trim()).ToList();
        Assert.Equal(100, messages.Distinct().Count());

        // The next write brings the folder back to what it keeps.
        var keeping = new CrashReports(Folder()) { Keep = 10 };
        var newest = keeping.Write("after", new InvalidOperationException("x"))!.Path;
        Assert.Equal(10, Directory.GetFiles(Folder()).Length);
        Assert.True(File.Exists(newest));
    }

    // Windows that fail while they are made

    private const string TwoPanels = """
        import Winshell
        import QtQuick
        ShellRoot {
          PanelWindow { objectName: "bar"; anchors.bottom: true; implicitHeight: 48 }
          PanelWindow { objectName: "other"; anchors.top: true; implicitHeight: 32 }
        }
        """;

    private static IEnumerable<string?> Names(FakePlatform platform) =>
        platform.Windows.Select(w => ((ShellWindow)w.Client).ObjectName).Order(StringComparer.Ordinal);

    [Fact]
    public void A_window_whose_native_window_cannot_be_made_is_reported_and_made_again_and_the_others_open()
    {
        using var files = new TestShell(("shell.qml", TwoPanels));
        var platform = new FakePlatform { FailCreations = 1 };
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log) { WatchFiles = false, Crashes = new CrashReports(Folder()) };

        Assert.True(host.Start());
        files.Loop.Process();

        Assert.Equal(["other"], Names(platform));
        Assert.Contains("the native window could not be made", File.ReadAllText(Assert.Single(Directory.GetFiles(Folder()))), StringComparison.Ordinal);
        files.Loop.Advance(TimeSpan.FromSeconds(1.1));
        Assert.Equal(["bar", "other"], Names(platform));
        Assert.All(platform.Windows, w => Assert.True(w.IsVisible));
    }

    [Fact]
    public void A_window_that_cannot_be_placed_is_disposed_reported_and_made_again_and_the_others_open()
    {
        using var files = new TestShell(("shell.qml", TwoPanels));
        var platform = new FakePlatform { FailPlacements = 1 };
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log) { WatchFiles = false, Crashes = new CrashReports(Folder()) };

        Assert.True(host.Start());
        files.Loop.Process();

        var placed = Assert.Single(platform.Windows);
        Assert.True(placed.IsVisible);
        Assert.Equal(2, platform.Creations);
        Assert.Contains("the panel could not be placed", File.ReadAllText(Assert.Single(Directory.GetFiles(Folder()))), StringComparison.Ordinal);
        files.Loop.Advance(TimeSpan.FromSeconds(1.1));
        Assert.Equal(["bar", "other"], Names(platform));
        Assert.All(platform.Windows, w => Assert.True(w.IsVisible && w.Panel is not null));
    }

    [Fact]
    public void A_reload_while_a_window_waits_to_open_again_makes_it_once()
    {
        using var files = new TestShell(("shell.qml", TwoPanels));
        var platform = new FakePlatform();
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log) { WatchFiles = false };
        Assert.True(host.Start());
        files.Loop.Process();
        var bar = platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "bar");
        bar.Client.OnFailed(new InvalidOperationException("broke"));
        Assert.Equal(["other"], Names(platform));

        host.Reload(hard: false);
        files.Loop.Process();
        Assert.Equal(["bar", "other"], Names(platform));
        var creations = platform.Creations;

        files.Loop.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(["bar", "other"], Names(platform));
        Assert.Equal(creations, platform.Creations);
        Assert.All(platform.Windows, w => Assert.Same(host.Root, ((ShellWindow)w.Client).Owner));
    }

    [Fact]
    public void A_frame_the_device_could_not_draw_is_reported_once_and_the_window_stays()
    {
        using var files = new TestShell(("shell.qml", TwoPanels));
        var platform = new FakePlatform();
        using var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, platform, files.Log) { WatchFiles = false, Crashes = new CrashReports(Folder()) };
        Assert.True(host.Start());
        files.Loop.Process();
        var bar = platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "bar");

        for (var i = 0; i < 5; i++)
            bar.Client.OnDrawFailed(Catch(FirstCaller));

        Assert.False(bar.Disposed);
        Assert.Contains("Where: PanelWindow \"bar\" in shell.qml on DISPLAY1 drawing", File.ReadAllText(Assert.Single(Directory.GetFiles(Folder()))), StringComparison.Ordinal);
    }
}
