using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class NativeCallbackTests
{
    [Fact]
    public void A_throwing_callback_returns_the_fallback_and_logs()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (level, category, message) => lines.Add($"{level} {category}: {message}") };

        var result = NativeCallback.Guard(log, "tray", () => throw new InvalidOperationException("broken"), (nint)7);

        Assert.Equal(7, result);
        Assert.Equal(["Warn tray: broken"], lines);
    }

    [Fact]
    public void A_throwing_action_is_logged_and_returns()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (_, category, message) => lines.Add($"{category}: {message}") };

        NativeCallback.Guard(log, "network", () => throw new InvalidOperationException("broken"));

        Assert.Equal(["network: broken"], lines);
    }

    [Fact]
    public void A_failing_log_does_not_escape()
    {
        var log = new Log { Sink = (_, _, _) => throw new IOException("disk full") };

        var result = NativeCallback.Guard(log, "hotkey", () => throw new InvalidOperationException("broken"), false);

        Assert.False(result);
    }

    [Fact]
    public void A_callback_that_returns_keeps_its_result()
    {
        var log = new Log { Sink = (_, _, _) => Assert.Fail("nothing failed") };

        Assert.True(NativeCallback.Guard(log, "hotkey", () => true, false));
    }
}
