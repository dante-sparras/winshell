using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A crash report still keeps host lines when the log stamps the date.</summary>
public sealed class DatedLogTailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-dated-log", Guid.NewGuid().ToString("N"));

    public DatedLogTailTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void A_dated_host_line_is_kept_and_a_dated_script_line_is_left_out()
    {
        var log = Path.Combine(_root, "log.txt");
        File.WriteAllLines(log,
        [
            "2026-10-29 12:00:00.000  INFO: printed by a script",
            "2026-10-29 12:00:01.000  INFO window: opened",
            "2026-10-29 12:00:02.000  INFO: console.log from the config",
        ]);
        var crashes = new CrashReports(Path.Combine(_root, "crashes")) { LogPath = log };

        var text = File.ReadAllText(crashes.Write("a window", new InvalidOperationException("x"))!.Path);

        Assert.Contains("2026-10-29 12:00:01.000  INFO window: opened", text, StringComparison.Ordinal);
        Assert.Contains("(2 lines of script output left out)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("printed by a script", text, StringComparison.Ordinal);
        Assert.DoesNotContain("console.log from the config", text, StringComparison.Ordinal);
    }
}
