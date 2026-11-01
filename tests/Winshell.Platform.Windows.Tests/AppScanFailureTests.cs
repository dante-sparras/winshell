using Winshell.Platform.Windows.Services.Apps;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A scan that throws is a finished failure, and the next file change scans again.</summary>
public class AppScanFailureTests
{
    [Fact]
    public async Task A_failed_scan_is_logged_and_the_next_change_scans_again()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winshell-app-scan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var calls = 0;
        var fail = 1;
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };
        try
        {
            using var catalog = new AppCatalog(
                [folder],
                () =>
                {
                    Interlocked.Increment(ref calls);
                    if (Interlocked.Exchange(ref fail, 0) == 1)
                        throw new InvalidCastException("scan broke");
                    return [];
                },
                () => new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromMilliseconds(30),
                watchPackages: false,
                registerIcons: false,
                log);
            catalog.Start();
            await catalog.Ready.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains(messages, m => m.Contains("scan broke", StringComparison.Ordinal));

            File.WriteAllText(Path.Combine(folder, "note.txt"), "x");
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Volatile.Read(ref calls) < 2)
            {
                Assert.True(DateTime.UtcNow < until, $"the next scan did not run (calls={Volatile.Read(ref calls)})");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
