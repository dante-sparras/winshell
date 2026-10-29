using Winshell.Platform.Windows.Services.Winland;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A Winland listener that throws is written to the instance log, with the stack, and does not escape.</summary>
public class WinlandListenerTests
{
    [Fact]
    public void A_listener_that_throws_is_logged_with_its_stack()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (level, category, message) => lines.Add($"{level} {category}: {message}") };
        WinlandConnection.RegisterLog(log);
        try
        {
            WinlandConnection.RaiseForTest(() => throw new InvalidOperationException("listener failed"));
        }
        finally
        {
            WinlandConnection.UnregisterLog(log);
        }

        Assert.Single(lines);
        Assert.StartsWith("Warn winland:", lines[0], StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", lines[0], StringComparison.Ordinal);
        Assert.Contains("listener failed", lines[0], StringComparison.Ordinal);
        Assert.Contains(" at ", lines[0], StringComparison.Ordinal);
    }
}
