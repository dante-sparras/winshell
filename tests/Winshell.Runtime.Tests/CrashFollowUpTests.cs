using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>The next start shows a fatal crash report once, and skips one older than the last clean exit.</summary>
public sealed class CrashFollowUpTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-crash-follow-up", Guid.NewGuid().ToString("N"));

    public CrashFollowUpTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void A_fatal_report_is_shown_once_and_a_later_one_is_shown_again()
    {
        var folder = Path.Combine(_dir, "crashes");
        Directory.CreateDirectory(folder);
        var stray = Path.Combine(folder, "crash-stray.txt");
        var draw = Path.Combine(folder, "crash-draw.txt");
        var fatal = Path.Combine(folder, "crash-fatal.txt");
        File.WriteAllText(stray, "notes\n");
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
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(draw, now.AddMinutes(-10));
        File.SetLastWriteTimeUtc(fatal, now);
        File.SetLastWriteTimeUtc(stray, now.AddMinutes(2));
        var state = Path.Combine(_dir, "state", "last-exit.toml");
        CrashFollowUp.MarkClean(state, new DateTimeOffset(now.AddHours(-1), TimeSpan.Zero));

        var pending = CrashFollowUp.Find(folder, state);

        Assert.Equal(fatal, pending?.Path);
        Assert.Equal("System.InvalidOperationException: boom", pending?.ExceptionLine);
        Assert.Contains("clean = ", File.ReadAllText(state), StringComparison.Ordinal);

        CrashFollowUp.MarkShown(state, fatal);
        Assert.Null(CrashFollowUp.Find(folder, state));

        var again = Path.Combine(folder, "crash-again.txt");
        File.WriteAllText(again, """
            Winshell crash report
            Winshell closed: yes
            Exception
            System.Exception: again
            """);
        File.SetLastWriteTimeUtc(again, now.AddMinutes(10));

        Assert.Equal(again, CrashFollowUp.Find(folder, state)?.Path);
        Assert.Equal("System.Exception: again", CrashFollowUp.Find(folder, state)?.ExceptionLine);
    }

    [Fact]
    public void A_report_older_than_the_clean_exit_is_not_shown()
    {
        var folder = Path.Combine(_dir, "old");
        Directory.CreateDirectory(folder);
        var fatal = Path.Combine(folder, "crash-old.txt");
        File.WriteAllText(fatal, """
            Winshell crash report
            Winshell closed: yes
            Exception
            System.Exception: old
            """);
        var written = File.GetLastWriteTimeUtc(fatal);
        var state = Path.Combine(_dir, "old-state.toml");
        CrashFollowUp.MarkClean(state, new DateTimeOffset(written.AddMinutes(1), TimeSpan.Zero));

        Assert.Null(CrashFollowUp.Find(folder, state));
        Assert.Null(CrashFollowUp.Find(Path.Combine(_dir, "missing"), state));
    }
}
