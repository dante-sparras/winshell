using Winshell.Runtime;

namespace Winshell.Runtime.Tests;

/// <summary>A callback that fails an assertion must fail the test. Any other exception is logged and the loop continues.</summary>
public class EventLoopAssertionTests
{
    [Fact]
    public void An_assertion_in_a_callback_fails_the_caller()
    {
        var loop = new ManualEventLoop();
        loop.Post(() => Assert.True(false));

        var error = Assert.ThrowsAny<Exception>(() => loop.Process());

        Assert.StartsWith("Xunit.Sdk.", error.GetType().FullName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_callback_exception_is_logged_and_later_work_still_runs()
    {
        var loop = new ManualEventLoop();
        var messages = new List<string>();
        Exception? failed = null;
        loop.Log = new Log { Sink = (_, _, message) => messages.Add(message), MinimumLevel = LogLevel.Debug };
        loop.Failed += error => failed = error;
        var ran = false;
        loop.Post(() => throw new InvalidOperationException("boom"));
        loop.Post(() => ran = true);

        loop.Process();

        Assert.True(ran);
        Assert.Contains(messages, message => message.Contains("boom", StringComparison.Ordinal));
        Assert.IsType<InvalidOperationException>(failed);
    }
}
