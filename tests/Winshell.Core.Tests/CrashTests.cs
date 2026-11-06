using System.Globalization;

namespace Winshell.Core.Tests;

/// <summary>Crash reports. Every report goes to a temp folder this class creates.</summary>
public sealed class CrashTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "winshell-crash-tests", Guid.NewGuid().ToString("N"));

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
        File.WriteAllLines(log, Enumerable.Range(1, 100).Select(i => $"12:00:00.000  INFO window: line {i}"));
        var crashes = new CrashReports(Path.Combine(_folder, "crashes"))
            { ShellId = "my-shell", ConfigPath = @"C:\config\MyShell.exe", LogPath = log };
        CrashReport? raised = null;
        crashes.Written += r => raised = r;

        var report = crashes.Write("PanelWindow in Taskbar.xaml", Thrown("the paint failed"));

        Assert.NotNull(report);
        Assert.Same(report, raised);
        Assert.False(report.Fatal);
        Assert.StartsWith(Path.Combine(_folder, "crashes"), report.Path, StringComparison.Ordinal);
        Assert.EndsWith("-panelwindow-in-taskbar-xaml.txt", report.Path, StringComparison.Ordinal);
        var text = File.ReadAllText(report.Path);
        Assert.StartsWith("Winshell crash report", text, StringComparison.Ordinal);
        Assert.Contains("This file stays on this PC.", text, StringComparison.Ordinal);
        Assert.Contains("Winshell: " + ProductVersion.Current.Text, text, StringComparison.Ordinal);
        Assert.Contains("Windows: ", text, StringComparison.Ordinal);
        Assert.Contains("Shell id: my-shell", text, StringComparison.Ordinal);
        Assert.Contains(@"Config: C:\config\MyShell.exe", text, StringComparison.Ordinal);
        Assert.Contains("Where: PanelWindow in Taskbar.xaml", text, StringComparison.Ordinal);
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

        Exception Same()
        {
            return Thrown("again");
        }

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
    public void A_report_is_read_back_by_the_time_in_its_name()
    {
        Directory.CreateDirectory(_folder);
        var older = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var newer = older.AddMinutes(5);
        var olderPath = Path.Combine(_folder, Name(older, "old"));
        var newerPath = Path.Combine(_folder, Name(newer, "new"));
        File.WriteAllText(olderPath, """
                                     Winshell crash report
                                     Winshell closed: yes
                                     Exception
                                     System.Exception: old
                                     """);
        File.WriteAllText(newerPath, """
                                     Winshell crash report
                                     Winshell closed: no
                                     Exception
                                     System.Exception: new
                                     """);
        File.WriteAllText(Path.Combine(_folder, "crash-stray.txt"), """
                                                                    Winshell crash report
                                                                    Winshell closed: yes
                                                                    """);
        File.SetLastWriteTimeUtc(olderPath, newer.AddHours(1));
        File.SetLastWriteTimeUtc(newerPath, older);

        var list = CrashReports.List(_folder);

        Assert.Null(list.FolderError);
        Assert.Equal(newerPath, list.Reports[0].Path);
        Assert.False(list.Reports[0].Closed);
        Assert.Equal("System.Exception: new", list.Reports[0].ExceptionLine);
        Assert.Equal(olderPath, list.Reports[1].Path);
        Assert.True(list.Reports[1].Closed);
        Assert.Equal("System.Exception: old", list.Reports[1].ExceptionLine);
        Assert.Equal(new DateTimeOffset(older, TimeSpan.Zero), list.Reports[1].Written);
    }

    private static string Name(DateTime utc, string slug)
    {
        return $"crash-{utc.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture)}Z-1-{slug}.txt";
    }
}
