using Winshell.Winland;

namespace Winshell.Services.Tests;

/// <summary>A Winland listener that throws is written to the instance log, with the stack, and does not escape or stop the next one.</summary>
public class WinlandListenerTests
{
    [Fact]
    public void A_listener_that_throws_is_logged_with_its_stack()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (level, category, message) => lines.Add($"{level} {category}: {message}") };
        var later = false;
        Action<int> handlers = _ => throw new InvalidOperationException("listener failed");
        handlers += _ => later = true;
        WinlandConnection.RegisterLog(log);
        try
        {
            WinlandConnection.Raise(handlers, 0);
        }
        finally
        {
            WinlandConnection.UnregisterLog(log);
        }

        Assert.True(later);
        Assert.Single(lines);
        Assert.StartsWith("Warn winland:", lines[0], StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", lines[0], StringComparison.Ordinal);
        Assert.Contains("listener failed", lines[0], StringComparison.Ordinal);
        Assert.Contains(" at ", lines[0], StringComparison.Ordinal);
    }
}
