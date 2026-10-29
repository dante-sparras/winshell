using Winshell.Platform.Windows.Diagnostics;
using static Winshell.Platform.Windows.Diagnostics.Doctor;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Doctor used to report a pass when a crash folder or an instance log could not be read, and it never opened a
/// crash report. These tests cover that evidence.
/// </summary>
public sealed class DoctorEvidenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-doctor-evidence", Guid.NewGuid().ToString("N"));

    public DoctorEvidenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void An_unreadable_instance_log_warns_instead_of_passing()
    {
        var log = Path.Combine(_dir, "log.txt");
        File.WriteAllText(log, "still running\n");
        using var locked = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None);

        var read = DoctorProbe.ReadInstanceLog(log);

        Assert.Null(read.LoadError);
        Assert.NotNull(read.Unreadable);
        Assert.Contains(log, read.Unreadable, StringComparison.Ordinal);
        var check = Config(@"C:\c\shell.qml", null, new ConfigScan(1, []), null, read.Unreadable);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("Could not read the running instance's log:", check.Message, StringComparison.Ordinal);
        Assert.Contains(log, check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_readable_instance_log_still_reports_the_last_load_error()
    {
        var log = Path.Combine(_dir, "real.txt");
        File.WriteAllText(log, "12:00:00.000 ERROR scene: Failed to load configuration: boom\n");

        var read = DoctorProbe.ReadInstanceLog(log);

        Assert.Equal("boom", read.LoadError);
        Assert.Null(read.Unreadable);
    }

    [Fact]
    public void A_missing_crash_folder_is_not_a_failure_to_read()
    {
        var read = DoctorProbe.ReadCrashReports(Path.Combine(_dir, "missing"));

        Assert.Null(read.Unreadable);
        Assert.Empty(read.Reports);
        Assert.Equal(CheckStatus.Pass, CrashesIn(read, DateTime.UtcNow, DoctorProbe.PeekCrash).Status);
    }

    [Fact]
    public void An_unreadable_crash_folder_warns()
    {
        var read = new DoctorProbe.CrashFolderRead([], @"C:\locked: Access is denied");

        var check = CrashesIn(read, DateTime.UtcNow);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains(@"Could not read the crash folder: C:\locked: Access is denied", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fatal_report_is_named_and_a_stray_file_is_ignored()
    {
        var stray = Path.Combine(_dir, "crash-stray.txt");
        var draw = Path.Combine(_dir, "crash-draw.txt");
        var fatal = Path.Combine(_dir, "crash-fatal.txt");
        File.WriteAllText(stray, "notes, not a report\n");
        File.WriteAllText(draw, """
            Winshell crash report
            Winshell closed: no
            Exception
            System.Exception: draw failed
            """);
        File.WriteAllText(fatal, """
            Winshell crash report
            Winshell closed: yes
            Exception
            System.InvalidOperationException: boom
            """);
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(draw, now);
        File.SetLastWriteTimeUtc(fatal, now.AddMinutes(-5));
        File.SetLastWriteTimeUtc(stray, now.AddMinutes(1));

        var read = DoctorProbe.ReadCrashReports(_dir);
        var check = CrashesIn(read, now.AddMinutes(2), DoctorProbe.PeekCrash);

        Assert.Null(read.Unreadable);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("2 crash reports", check.Message, StringComparison.Ordinal);
        Assert.Contains(draw, check.Message, StringComparison.Ordinal);
        Assert.Contains("Winshell closed: yes.", check.Message, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: boom", check.Message, StringComparison.Ordinal);
        Assert.Contains(fatal, check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("stray", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("draw failed", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crash_file_that_cannot_be_opened_warns()
    {
        var blocked = Path.Combine(_dir, "crash-blocked.txt");
        Directory.CreateDirectory(blocked);

        var peek = DoctorProbe.PeekCrash(blocked);

        Assert.NotNull(peek.Unreadable);
        Assert.Contains(blocked, peek.Unreadable, StringComparison.Ordinal);
        var check = Crashes([(blocked, DateTime.UtcNow)], DateTime.UtcNow, _ => peek);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("Could not read a crash report:", check.Message, StringComparison.Ordinal);
    }
}
