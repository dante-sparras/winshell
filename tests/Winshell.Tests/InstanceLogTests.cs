namespace Winshell.Tests;

/// <summary>
/// The instance log rolls instead of growing without limit, a warning is on disk at once, and an ordinary line a
/// moment later.
/// </summary>
public sealed class InstanceLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public InstanceLogTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void A_warning_lands_immediately_and_a_full_file_rolls()
    {
        var path = Path.Combine(_dir, "log.txt");
        var previous = Path.Combine(_dir, "log.1.txt");
        using (var log = new InstanceLog(path, 100, Timeout.InfiniteTimeSpan))
        {
            log.Write(LogLevel.Info, "info-still-buffered");
            Assert.DoesNotContain("info-still-buffered", Read(path), StringComparison.Ordinal);

            log.Write(LogLevel.Warn, "warned-now");
            var afterWarn = Read(path);
            Assert.Contains("info-still-buffered", afterWarn, StringComparison.Ordinal);
            Assert.Contains("warned-now", afterWarn, StringComparison.Ordinal);

            for (var i = 0; i < 30; i++)
                log.Write(LogLevel.Warn, "pad-" + i + "-xxxxxxxxxxxxxxxx");

            Assert.True(File.Exists(previous));
            Assert.True(new FileInfo(path).Length < 100 + 80);
            var kept = Read(path) + Read(previous);
            Assert.Contains("pad-29-", kept, StringComparison.Ordinal);
            Assert.DoesNotContain("pad-0-", kept, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_ordinary_line_lands_a_moment_after_it_is_written()
    {
        var path = Path.Combine(_dir, "log.txt");
        using var log = new InstanceLog(path, flushAfter: TimeSpan.FromMilliseconds(20));
        log.Write(LogLevel.Info, "first");
        Wait.Until(() => Read(path).Contains("first", StringComparison.Ordinal), "the first line on disk");

        // Nothing was left to write, so the timer stopped. The next line starts it again.
        log.Write(LogLevel.Info, "second");
        Wait.Until(() => Read(path).Contains("second", StringComparison.Ordinal), "the second line on disk");
    }

    [Fact]
    public void A_timed_flush_that_cannot_reopen_the_file_does_not_end_the_process_and_logging_resumes()
    {
        var path = Path.Combine(_dir, "log.txt");
        var time = new FakeTimeProvider();
        var delay = TimeSpan.FromMilliseconds(500);
        using var log = new InstanceLog(path, 10, delay, time);
        log.Write(LogLevel.Warn, "fills-the-file-past-the-cap");

        // Move the file the log holds aside and keep a new one at its path open, so the roll cannot move or reopen it.
        File.Move(path, Path.Combine(_dir, "aside.txt"));
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            log.Write(LogLevel.Info, "lost-with-the-failed-roll");
            // The timer's flush runs here, on this thread, and fails.
            time.Advance(delay);
        }

        log.Write(LogLevel.Info, "after-the-file-is-free");
        time.Advance(delay);
        Assert.Contains("after-the-file-is-free", Read(path), StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        if (!File.Exists(path))
            return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
}
