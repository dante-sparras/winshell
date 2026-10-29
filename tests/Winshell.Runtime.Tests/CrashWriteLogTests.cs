using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A crash report that cannot be written, and a handler that throws, are logged instead of dropped.</summary>
public sealed class CrashWriteLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-crash-write-log", Guid.NewGuid().ToString("N"));
    private readonly List<string> _lines = [];

    public CrashWriteLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private CrashReports Reports(string folder) => new(folder)
    {
        Log = new Log { Sink = (level, category, message) => _lines.Add($"{level} {category}: {message}") },
    };

    [Fact]
    public void A_report_that_cannot_be_written_is_logged_and_still_returns_null()
    {
        var file = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(file, "x");

        Assert.Null(Reports(file).Write("here", new InvalidOperationException("disk full")));

        var line = Assert.Single(_lines);
        Assert.StartsWith("Error shell: Could not write a crash report:", line);
        Assert.True(line.Length > "Error shell: Could not write a crash report:".Length);
    }

    [Fact]
    public void A_handler_that_throws_is_logged_and_the_report_is_kept()
    {
        var crashes = Reports(Path.Combine(_dir, "crashes"));
        crashes.Written += _ => throw new InvalidOperationException("notice failed");

        var report = crashes.Write("here", new InvalidOperationException("x"));

        Assert.NotNull(report);
        Assert.True(File.Exists(report.Path));
        var line = Assert.Single(_lines);
        Assert.Equal("Error shell: Could not write a crash report: notice failed", line);
    }

    [Fact]
    public void A_duplicate_report_is_not_logged_as_a_write_failure()
    {
        var crashes = Reports(Path.Combine(_dir, "once"));
        var error = new InvalidOperationException("x");

        Assert.NotNull(crashes.WriteOnce("here", error));
        Assert.Null(crashes.WriteOnce("here", error));

        Assert.Empty(_lines);
    }
}
