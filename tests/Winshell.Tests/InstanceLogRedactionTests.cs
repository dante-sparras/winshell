using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Winshell.Tests;

/// <summary>
/// The instance log outlives the run, so a secret is taken out of each line as it is written to it. The shell
/// crashes through <c>WINSHELL_TEST_CRASH</c> once its config has loaded, and its report names the log.
/// </summary>
public sealed partial class InstanceLogRedactionTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    [GeneratedRegex(@"^Log: (?<path>.+), last \d+ lines\r?$", RegexOptions.Multiline)]
    private static partial Regex LogLine();

    [Fact]
    public async Task A_secret_given_to_a_flag_is_not_written_to_the_instance_log()
    {
        var config = Path.Combine(_dir, "config");
        var crashes = Path.Combine(_dir, "crashes");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "shell.qml"), """
            import Winshell
            import QtQuick
            ShellRoot {
              Component.onCompleted: console.warn("sync --password SENTINEL-PASSWORD")
            }
            """);
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(config);
        info.Environment["WINSHELL_TEST_CRASH"] = crashes;

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
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
        await error;
        await output;

        var report = File.ReadAllText(Assert.Single(Directory.GetFiles(crashes)));
        var logged = File.ReadAllText(LogLine().Match(report).Groups["path"].Value);
        Assert.Contains("sync --password [redacted]", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", report, StringComparison.Ordinal);
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
