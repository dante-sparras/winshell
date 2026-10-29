using System.Diagnostics;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public sealed class LogRedactionPerformanceTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task A_long_log_line_does_not_block_logging()
    {
        CrashRedaction.Redact("warm up --password value");
        var text = new string('a', 100_000);
        var watch = Stopwatch.StartNew();

        var redacted = await Task.Run(() => CrashRedaction.Redact(text)).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(text, redacted);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Redaction took {watch.Elapsed}.");
    }
}
