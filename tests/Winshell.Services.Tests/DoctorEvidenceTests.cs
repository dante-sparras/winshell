using System.Globalization;
using Winshell.Diagnostics;
using static Winshell.Diagnostics.DoctorCrashes;

namespace Winshell.Services.Tests;

/// <summary>
/// A crash folder that cannot be read warns, and a fatal report is named. These tests cover that evidence.
/// </summary>
public sealed class DoctorEvidenceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "winshell-doctor-evidence", Guid.NewGuid().ToString("N"));

    public DoctorEvidenceTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void A_missing_crash_folder_is_not_a_failure_to_read()
    {
        var read = CrashReports.List(Path.Combine(_dir, "missing"));

        Assert.Null(read.FolderError);
        Assert.Empty(read.Reports);
        Assert.Equal(CheckStatus.Pass, CrashesIn(read, DateTime.UtcNow).Status);
    }

    [Fact]
    public void An_unreadable_crash_folder_warns()
    {
        var read = new CrashList([], @"C:\locked: Access is denied", null);

        var check = CrashesIn(read, DateTime.UtcNow);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains(@"Could not read the crash folder: C:\locked: Access is denied", check.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_fatal_report_is_named_and_a_stray_file_is_ignored()
    {
        var now = DateTime.UtcNow;
        var stray = Path.Combine(_dir, "crash-stray.txt");
        var draw = Report(_dir, now, "draw");
        var fatal = Report(_dir, now.AddMinutes(-5), "fatal");
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

        var read = CrashReports.List(_dir);
        var check = CrashesIn(read, now.AddMinutes(2));

        Assert.Null(read.FolderError);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("2 crash reports", check.Message, StringComparison.Ordinal);
        Assert.Contains(draw, check.Message, StringComparison.Ordinal);
        Assert.Contains("Winshell closed: yes.", check.Message, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: boom", check.Message, StringComparison.Ordinal);
        Assert.Contains(fatal, check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("stray", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("draw failed", check.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Winshell crash reports")]
    [InlineData("Winshell crash report ")]
    public void A_file_whose_first_line_is_not_exactly_the_heading_is_not_a_report(string first)
    {
        var path = Report(_dir, DateTime.UtcNow, "lookalike");
        File.WriteAllText(path, first + "\nWinshell closed: yes\n");

        Assert.Empty(CrashReports.List(_dir).Reports);
    }

    [Fact]
    public void A_crash_file_that_cannot_be_opened_warns()
    {
        var blocked = Report(_dir, DateTime.UtcNow, "blocked");
        using (new FileStream(blocked, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            var read = CrashReports.List(_dir);

            Assert.NotNull(read.FileError);
            Assert.Contains(blocked, read.FileError, StringComparison.Ordinal);
            var check = CrashesIn(read, DateTime.UtcNow);
            Assert.Equal(CheckStatus.Warn, check.Status);
            Assert.Contains("Could not read a crash report:", check.Message, StringComparison.Ordinal);
        }
    }

    private static string Report(string folder, DateTime utc, string slug)
    {
        var name =
            $"crash-{utc.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)}Z-1-{slug}.txt";
        return Path.Combine(folder, name);
    }
}
