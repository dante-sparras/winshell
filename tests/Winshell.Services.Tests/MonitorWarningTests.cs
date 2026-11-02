namespace Winshell.Services.Tests;

public sealed class MonitorWarningTests
{
    [Fact]
    public void A_failure_that_lasts_is_warned_once_until_a_poll_succeeds()
    {
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };
        var warning = new MonitorWarning(() => log, "bluetooth");

        warning.Failed(new InvalidOperationException("radio broke"));
        warning.Failed(new InvalidOperationException("radio broke"));
        warning.Failed(new InvalidOperationException("radio broke"));
        Assert.Equal(["bluetooth: radio broke"], messages);

        warning.Failed(new InvalidOperationException("radio gone"));
        warning.Succeeded();
        warning.Failed(new InvalidOperationException("radio gone"));
        Assert.Equal(["bluetooth: radio broke", "bluetooth: radio gone", "bluetooth: radio gone"], messages);
    }
}
