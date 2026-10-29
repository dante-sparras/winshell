using Winshell.Runtime;

namespace Winshell.Tests;

/// <summary>The instance log rolls instead of growing without limit, and a warning is on disk at once.</summary>
public sealed class InstanceLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public InstanceLogTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void A_warning_lands_immediately_and_a_full_file_rolls()
    {
        var path = Path.Combine(_dir, "log.txt");
        var previous = Path.Combine(_dir, "log.1.txt");
        using (var log = new InstanceLog(path, cap: 100, flushEvery: Timeout.InfiniteTimeSpan))
        {
            log.Write(LogLevel.Info, "info-still-buffered");
            Assert.DoesNotContain("info-still-buffered", Read(path), StringComparison.Ordinal);

            log.Write(LogLevel.Warn, "warned-now");
            var afterWarn = Read(path);
            Assert.Contains("info-still-buffered", afterWarn, StringComparison.Ordinal);
            Assert.Contains("warned-now", afterWarn, StringComparison.Ordinal);

            for (var i = 0; i < 30; i++)
                log.Write(LogLevel.Warn, "pad-" + i + "-xxxxxxxxxxxxxxxx");

            Assert.True(File.Exists(previous));
            Assert.True(new FileInfo(path).Length < 100 + 80);
            var kept = Read(path) + Read(previous);
            Assert.Contains("pad-29-", kept, StringComparison.Ordinal);
            Assert.DoesNotContain("pad-0-", kept, StringComparison.Ordinal);
        }
    }

    private static string Read(string path)
    {
        if (!File.Exists(path))
            return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
