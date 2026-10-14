using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

[TestClass]
public sealed class TurnFloorTests
{
    [TestMethod]
    public void A_lone_window_event_runs_its_turn_at_once()
    {
        var floor = new TurnFloor();

        floor.Wait(now: 5_000).Should().Be(0);

        floor.Ran(now: 5_000);
        floor.Wait(now: 5_000 + TurnFloor.FloorMs).Should().Be(0);
    }

    [TestMethod]
    public void An_event_close_behind_a_turn_waits_out_the_rest_of_the_floor()
    {
        var floor = new TurnFloor();
        floor.Ran(now: 5_000);

        floor.Wait(now: 5_000).Should().Be(TurnFloor.FloorMs);
        floor.Wait(now: 5_030).Should().Be(TurnFloor.FloorMs - 30);
        floor.Wait(now: 5_000 + TurnFloor.FloorMs - 1).Should().Be(1);
    }

    [TestMethod]
    public void A_clock_that_reads_earlier_than_the_last_turn_never_waits_longer_than_the_floor()
    {
        var floor = new TurnFloor();
        floor.Ran(now: 5_000);

        floor.Wait(now: 4_000).Should().Be(TurnFloor.FloorMs);
    }
}
