using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Winshell.Core.Tests;

/// <summary>
/// What a crash report must not do: keep a secret, hide a different crash, lose a report to another writer, show up
/// half written, keep the wrong ones, or delete a file that is not a report. And windows that fail while they are first made or placed.
/// Every file is under a temp folder this class creates.
/// </summary>
public sealed class CrashReportSafetyTests : IDisposable
{
    /// <summary>How many reports the folder keeps, as docs/development.md says.</summary>
    private const int Keep = 50;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "winshell-crash-tests", Guid.NewGuid().ToString("N"));

    public CrashReportSafetyTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // A junction is removed as a link, so what it points at is deleted once, through its own path.
        foreach (var dir in Directory.EnumerateDirectories(_root))
            if (new DirectoryInfo(dir).LinkTarget is not null)
                Directory.Delete(dir);
        Directory.Delete(_root, true);
    }

    private string Folder(string name = "crashes")
    {
        return Path.Combine(_root, name);
    }

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
    private static void Helper()
    {
        throw new InvalidOperationException("from the helper");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FirstCaller()
    {
        Helper();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SecondCaller()
    {
        Helper();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FirstTask()
    {
        throw new InvalidOperationException("first task");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SecondTask()
    {
        throw new InvalidOperationException("second task");
    }

    // Privacy

    [Fact]
    public void Secrets_in_the_exception_and_its_inner_exceptions_are_taken_out()
    {
        var inner = Catch(() =>
            throw new InvalidOperationException(
                """request failed: api_key=SENTINEL-INNER-KEY-1 and "client_secret": "SENTINEL-JSON-2" """));
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

    [Theory]
    [InlineData("Cookie: a=b; c=d", "Cookie: [redacted]")]
    [InlineData("Set-Cookie: session=abc; Path=/; Secure", "Set-Cookie: [redacted]")]
    [InlineData("password = correct horse battery", "password = [redacted]")]
    [InlineData("passphrase: correct horse battery", "passphrase: [redacted]")]
    [InlineData("Proxy-Authorization: Basic abc def", "Proxy-Authorization: [redacted]")]
    [InlineData("password = correct horse\nnext line", "password = [redacted]\nnext line")]
    public void A_value_with_spaces_is_masked_to_the_end_of_its_line(string line, string expected)
    {
        Assert.Equal(expected, CrashRedaction.Redact(line));
    }

    [Fact]
    public void A_quoted_password_keeps_what_follows_it()
    {
        var redacted = CrashRedaction.Redact("password=\"hunter2\" and more");

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.Contains("and more", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_text_is_left_alone()
    {
        const string text = "at Winshell.Runtime.Core.SignalSlot.Emit(Object[] args) in C:\\src\\Signals.cs:line 78\n" +
                            "Passed: 5, Author: Dante, shell id 5f4dcc3b5aa765d61d8327deb882cf99, crash-20261029-140312-1234567Z-4321-panelwindow-in-taskbar-xaml.txt";

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
            "crash-20000101-000000-0000000Z-1-old.txt"
        };
        foreach (var name in others)
            File.WriteAllText(Path.Combine(folder, name), "not a crash report");
        var crashes = new CrashReports(folder);

        var written = Enumerable.Range(0, Keep + 1)
            .Select(i => crashes.Write($"report {i}", new InvalidOperationException("x"))!.Path).ToList();

        var left = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(others.Concat(written.Skip(1).Select(Path.GetFileName)).Order(StringComparer.Ordinal), left);
    }

    [Fact]
    public void Nothing_is_deleted_through_a_junction()
    {
        var target = Folder("elsewhere");
        Directory.CreateDirectory(target);
        // Reports a user moved there, or another program's files: none is the crash folder's to delete.
        var crashes = new CrashReports(target);
        var moved = Enumerable.Range(0, 3)
            .Select(i => crashes.Write($"moved {i}", new InvalidOperationException("x"))!.Path).ToList();
        File.WriteAllText(Path.Combine(target, "0000-user-notes.txt"), "keep me");
        var link = Folder();
        Junction(link, target);

        // Enough to push the moved reports past what the folder keeps.
        var through = new CrashReports(link);
        for (var i = 0; i < Keep; i++)
            Assert.NotNull(through.Write("through the junction", new InvalidOperationException("x")));

        Assert.All(moved, path => Assert.True(File.Exists(path)));
        Assert.True(File.Exists(Path.Combine(target, "0000-user-notes.txt")));
    }

    private static void Junction(string link, string target)
    {
        ChildProcess.Run("cmd.exe", "/c", "mklink", "/J", link, target).AssertExitCode(0);
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void Reports_written_in_the_same_instant_are_kept_newest_first()
    {
        var instant = new DateTimeOffset(2031, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var crashes = new CrashReports(Folder()) { Time = new FakeTimeProvider(instant) };

        var paths = Enumerable.Range(0, Keep + 20)
            .Select(_ => crashes.Write("same instant", new InvalidOperationException("x"))!.Path).ToList();

        Assert.Equal(paths.TakeLast(Keep).Order(StringComparer.OrdinalIgnoreCase),
            Directory.GetFiles(Folder()).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Retention_follows_the_utc_instant_across_a_clock_change()
    {
        // Daylight saving ends: 02:30 at +02:00 is 00:30 UTC; twenty minutes on the wall later it is 02:10 at +01:00, 01:10 UTC.
        var time = new FakeTimeProvider(new DateTimeOffset(2031, 10, 26, 2, 30, 0, TimeSpan.FromHours(2))
            .ToUniversalTime());
        time.SetLocalTimeZone(ZoneAt(2));
        var crashes = new CrashReports(Folder()) { Time = time };

        var before = Enumerable.Range(0, Keep)
            .Select(_ => crashes.Write("before", new InvalidOperationException("x"))!.Path).ToList();
        time.SetUtcNow(new DateTimeOffset(2031, 10, 26, 2, 10, 0, TimeSpan.FromHours(1)).ToUniversalTime());
        time.SetLocalTimeZone(ZoneAt(1));
        var later = crashes.Write("after", new InvalidOperationException("x"))!.Path;

        Assert.Equal(before.Skip(1).Append(later).Order(StringComparer.OrdinalIgnoreCase),
            Directory.GetFiles(Folder()).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains("Time: 2031-10-26 02:10:00.000 +01:00", File.ReadAllText(later), StringComparison.Ordinal);
    }

    private static TimeZoneInfo ZoneAt(int hours)
    {
        return TimeZoneInfo.CreateCustomTimeZone($"UTC+{hours}", TimeSpan.FromHours(hours), null, null);
    }

    [Fact]
    public void Two_writers_sharing_the_folder_never_overwrite_each_other()
    {
        // As two instances would: separate writers, each with its own lock, on one folder at one instant. Together
        // they write no more than the folder keeps, so every report is still there to count.
        var instant = new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var writers = Enumerable.Range(0, 4).Select(_ => new CrashReports(Folder())
            { Time = new FakeTimeProvider(instant) }).ToList();
        var each = Keep / writers.Count;
        var total = each * writers.Count;
        using var start = new Barrier(writers.Count);

        var paths = writers.AsParallel().WithDegreeOfParallelism(writers.Count).SelectMany((writer, w) =>
        {
            start.SignalAndWait();
            return Enumerable.Range(0, each)
                .Select(i => writer.Write($"writer {w}", new InvalidOperationException($"{w}/{i}"))!.Path).ToList();
        }).ToList();

        Assert.Equal(total, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(total, Directory.GetFiles(Folder()).Length);
        var messages = Directory.GetFiles(Folder()).Select(File.ReadAllText).Select(t =>
            t[t.IndexOf("InvalidOperationException: ", StringComparison.Ordinal)..].Split('\n')[0].Trim()).ToList();
        Assert.Equal(total, messages.Distinct().Count());

        // Writing past what the folder keeps brings it back to that.
        var keeping = new CrashReports(Folder());
        var newest = "";
        for (var i = total; i <= Keep; i++)
            newest = keeping.Write("after", new InvalidOperationException("x"))!.Path;
        Assert.Equal(Keep, Directory.GetFiles(Folder()).Length);
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void A_reader_of_the_folder_only_ever_finds_whole_reports()
    {
        // Read every file in the folder while reports go in, refusing to share with a writer, so a report still being
        // written fails the open. Delete is shared: a file just renamed into a folder is still open to the rename for
        // an instant. A long message keeps each write busy long enough for the reader to land in the middle of it.
        var folder = Folder();
        Directory.CreateDirectory(folder);
        var crashes = new CrashReports(folder);
        var message = string.Join('\n', Enumerable.Range(0, 5_000).Select(i => $"line {i} of a long message"));
        var lengths = new ConcurrentDictionary<string, ConcurrentQueue<int>>(StringComparer.OrdinalIgnoreCase);
        var failures = new ConcurrentQueue<string>();
        var writing = true;
        var reader = new Thread(() =>
            {
                while (Volatile.Read(ref writing))
                    foreach (var file in Directory.GetFiles(folder))
                        try
                        {
                            using var report = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read,
                                FileShare.Read | FileShare.Delete));
                            lengths.GetOrAdd(file, _ => new ConcurrentQueue<int>()).Enqueue(report.ReadToEnd().Length);
                        }
                        catch (Exception e)
                        {
                            failures.Enqueue($"{Path.GetFileName(file)}: {e.GetType().Name}: {e.Message}");
                        }
            })
            { IsBackground = true };
        reader.Start();

        var paths = Enumerable.Range(0, 5)
            .Select(i => crashes.Write($"report {i}", new InvalidOperationException(message))!.Path).ToList();
        Volatile.Write(ref writing, false);
        reader.Join();

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Distinct()));
        Assert.Equal(paths.Order(StringComparer.OrdinalIgnoreCase),
            Directory.GetFiles(folder).Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (file, seen) in lengths)
            Assert.All(seen, length => Assert.Equal(File.ReadAllText(file).Length, length));
    }
}
