using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Winshell.Tests;

/// <summary>
/// A winshell.exe whose shell crashes after its config loaded, as an exception escaping the loop would: it writes one
/// fatal crash report before it closes its log, and reporting it again as the process ends throws nothing. The
/// report goes to a temp folder through <c>WINSHELL_TEST_CRASH</c>, never to the user's crash folder.
/// </summary>
public sealed partial class FatalCrashTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    [GeneratedRegex(@"^Log: (?<path>.+), last \d+ lines\r?$", RegexOptions.Multiline)]
    private static partial Regex LogLine();

    [Fact]
    public async Task A_crash_that_ends_the_shell_writes_one_report_before_the_log_closes()
    {
        var config = Path.Combine(_dir, "config");
        var crashes = Path.Combine(_dir, "crashes");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "shell.qml"), """
            import Winshell
            ShellRoot {}
            """);
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(config);
        info.Environment.Remove("WINSHELL_DISABLE_FILE_WATCHER");
        info.Environment["WINSHELL_TEST_CRASH"] = crashes;

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                Assert.Fail("winshell did not crash within 30 seconds");
            }
        }
        var stderr = await error + await output;

        Assert.NotEqual(0, process.ExitCode);
        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(crashes)));
        Assert.Contains("Winshell closed: yes", report, StringComparison.Ordinal);
        Assert.Contains("Where: Winshell", report, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: WINSHELL_TEST_CRASH asked Winshell to crash after its config loaded.", report, StringComparison.Ordinal);
        Assert.Contains("INFO shell: Launching config", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectDisposedException", stderr, StringComparison.Ordinal);
        // The crash and its report are in the log, written before the log file closed.
        var log = LogLine().Match(report).Groups["path"].Value;
        var logged = File.ReadAllText(log);
        Assert.Contains("FATAL: Winshell crashed: System.InvalidOperationException", logged, StringComparison.Ordinal);
        Assert.Contains("Wrote a crash report to", logged, StringComparison.Ordinal);
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
