using Winshell.Winland;

namespace Winshell.Services.Tests;

/// <summary>Looking for Winland waits longer each time it is not there, and never more often than every 30 s.</summary>
public sealed class QuietPollingTests
{
    [Fact]
    public void Looking_for_winland_backs_off_from_3_to_30_seconds()
    {
        var poll = TimeSpan.FromSeconds(3);
        List<double> waits = [];
        for (var i = 0; i < 6; i++)
        {
            waits.Add(poll.TotalSeconds);
            poll = WinlandConnection.NextPoll(poll);
        }
        Assert.Equal(new double[] { 3, 6, 12, 24, 30, 30 }, waits);
    }
}
