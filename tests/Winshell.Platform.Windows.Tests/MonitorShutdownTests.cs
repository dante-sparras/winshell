using Winshell.Platform.Windows.Services;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class MonitorShutdownTests
{
    [Fact]
    public void A_failure_that_lasts_is_warned_once_until_a_poll_succeeds()
    {
        var messages = new List<string>();
        using var engine = new QmlEngine(StandardModules.Create(), new ManualEventLoop(),
            new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") });
        var service = new QObject { Engine = engine };
        var warning = new MonitorWarning(() => service.Engine?.Log ?? Log.Default, "bluetooth");

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
