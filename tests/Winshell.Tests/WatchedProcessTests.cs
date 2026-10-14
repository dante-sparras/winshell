using FluentAssertions;

namespace Winshell.Tests;

/// <summary>When the shell's watcher should stop waiting on a process id.</summary>
[TestClass]
public sealed class WatchedProcessTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 14, 9, 58, 14, TimeSpan.FromHours(2));

    [TestMethod]
    public void A_watched_process_is_gone_when_its_id_is_missing_it_has_exited_or_the_id_was_reused()
    {
        WatchedProcess.Gone(Started, null).Should().BeTrue();
        WatchedProcess.Gone(Started, new WatchedProcess.Sight(Started, HasExited: true)).Should().BeTrue();
        WatchedProcess.Gone(Started, new WatchedProcess.Sight(Started.AddMinutes(1), HasExited: false)).Should().BeTrue();

        WatchedProcess.Gone(Started, new WatchedProcess.Sight(Started, HasExited: false)).Should().BeFalse();
        WatchedProcess.Gone(null, new WatchedProcess.Sight(Started, HasExited: false)).Should().BeFalse();
    }
}
