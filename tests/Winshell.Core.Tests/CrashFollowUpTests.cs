using System.Globalization;
using System.Text;

namespace Winshell.Core.Tests;

/// <summary>The next start shows a fatal crash report once, and skips one older than the last clean exit.</summary>
public sealed class CrashFollowUpTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "winshell-crash-follow-up", Guid.NewGuid().ToString("N"));

    public CrashFollowUpTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void A_fatal_report_is_shown_once_and_a_later_one_is_shown_again()
    {
        var folder = Path.Combine(_dir, "crashes");
        Directory.CreateDirectory(folder);
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var stray = Path.Combine(folder, "crash-stray.txt");
        var draw = Report(folder, now.AddMinutes(-10), "draw");
        var fatal = Report(folder, now, "fatal");
        File.WriteAllText(stray, """
                                 Winshell crash report
                                 Winshell closed: yes
                                 Exception
                                 System.Exception: stray
                                 """);
        File.WriteAllText(draw, """
                                Winshell crash report
                                Winshell closed: no
                                Exception
                                System.Exception: draw
                                """);
        File.WriteAllText(fatal, """
                                 Winshell crash report
                                 Winshell closed: yes
                                 Exception
                                 System.InvalidOperationException: boom
                                 """);
        File.SetLastWriteTimeUtc(stray, now.AddHours(1));
        File.SetLastWriteTimeUtc(draw, now.AddHours(1));
        File.SetLastWriteTimeUtc(fatal, now.AddHours(-1));
        var state = Path.Combine(_dir, "state", "last-exit.toml");
        CrashReports.MarkClean(state, new DateTimeOffset(now.AddHours(-1), TimeSpan.Zero));

        var pending = CrashReports.Find(folder, state);

        Assert.Equal(fatal, pending?.Path);
        Assert.Equal("System.InvalidOperationException: boom", pending?.ExceptionLine);
        Assert.Contains("clean = ", File.ReadAllText(state), StringComparison.Ordinal);

        CrashReports.MarkShown(state, fatal);
        Assert.Null(CrashReports.Find(folder, state));

        var again = Report(folder, now.AddMinutes(10), "again");
        File.WriteAllText(again, """
                                 Winshell crash report
                                 Winshell closed: yes
                                 Exception
                                 System.Exception: again
                                 """);

        Assert.Equal(again, CrashReports.Find(folder, state)?.Path);
        Assert.Equal("System.Exception: again", CrashReports.Find(folder, state)?.ExceptionLine);
    }

    [Fact]
    public void A_report_older_than_the_clean_exit_is_not_shown()
    {
        var folder = Path.Combine(_dir, "old");
        Directory.CreateDirectory(folder);
        var written = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var fatal = Report(folder, written, "old");
        File.WriteAllText(fatal, """
                                 Winshell crash report
                                 Winshell closed: yes
                                 Exception
                                 System.Exception: old
                                 """);
        File.SetLastWriteTimeUtc(fatal, written.AddDays(2));
        var state = Path.Combine(_dir, "old-state.toml");
        CrashReports.MarkClean(state, new DateTimeOffset(written.AddMinutes(1), TimeSpan.Zero));

        Assert.Null(CrashReports.Find(folder, state));
        Assert.Null(CrashReports.Find(Path.Combine(_dir, "missing"), state));
    }

    [Fact]
    public void The_state_file_is_written_without_a_byte_order_mark()
    {
        var state = Path.Combine(_dir, "mark.toml");
        CrashReports.MarkClean(state, DateTimeOffset.UnixEpoch);

        var text = File.ReadAllText(state);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), File.ReadAllBytes(state).Length);
    }

    private static string Report(string folder, DateTime utc, string slug)
    {
        var name =
            $"crash-{utc.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)}Z-1-{slug}.txt";
        return Path.Combine(folder, name);
    }
}
