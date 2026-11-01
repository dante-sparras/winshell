using System.Diagnostics;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public sealed class LogRedactionPerformanceTests
{
    private const int Runs = 3;

    [Fact]
    [Trait("Category", "Performance")]
    public async Task A_long_log_line_does_not_block_logging()
    {
        CrashRedaction.Redact("warm up --password value");
        var text = new string('a', 100_000);

        // A busy runner can stall any one run, so the budget is for the fastest. Runaway backtracking makes every run slow.
        var fastest = await Task.Run(() => Fastest(text)).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(fastest < TimeSpan.FromSeconds(2), $"Redaction took {fastest}.");
    }

    private static TimeSpan Fastest(string text)
    {
        var fastest = TimeSpan.MaxValue;
        for (var i = 0; i < Runs; i++)
        {
            var watch = Stopwatch.StartNew();
            var redacted = CrashRedaction.Redact(text);
            watch.Stop();
            Assert.Equal(text, redacted);
            if (watch.Elapsed < fastest)
                fastest = watch.Elapsed;
        }
        return fastest;
    }
}
