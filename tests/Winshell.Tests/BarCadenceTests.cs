using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

[TestClass]
public sealed class BarCadenceTests
{
    private readonly BarCadence _cadence = new();

    [TestMethod]
    public void An_idle_desktop_turns_just_after_the_next_wall_clock_second()
    {
        _cadence.Next(now: 10_000, busy: false, away: false, wallSecond: 0, wallMillisecond: 0).Should().Be(1000 + BarCadence.SecondSkewMs);
        _cadence.Next(now: 10_000, busy: false, away: false, wallSecond: 0, wallMillisecond: 400).Should().Be(600 + BarCadence.SecondSkewMs);
    }

    [TestMethod]
    public void An_idle_turn_never_comes_sooner_than_the_fast_pace()
    {
        var wait = _cadence.Next(now: 10_000, busy: false, away: false, wallSecond: 0, wallMillisecond: 995);

        wait.Should().Be(1015);
    }

    [TestMethod]
    public void An_event_keeps_the_fast_pace_for_a_while_then_lets_it_go()
    {
        _cadence.Activity(now: 10_000).Should().BeTrue();

        _cadence.Next(now: 10_000, busy: false, away: false, wallSecond: 0, wallMillisecond: 0).Should().Be(BarCadence.FastMs);
        _cadence.Next(now: 10_000 + BarCadence.ActiveMs - 1, busy: false, away: false, wallSecond: 0, wallMillisecond: 0).Should().Be(BarCadence.FastMs);
        _cadence.Next(now: 10_000 + BarCadence.ActiveMs, busy: false, away: false, wallSecond: 0, wallMillisecond: 0).Should().BeGreaterThan(BarCadence.FastMs);
    }

    [TestMethod]
    public void A_second_event_while_active_does_not_ask_to_shorten_the_wait()
    {
        _cadence.Activity(now: 10_000).Should().BeTrue();
        _cadence.Activity(now: 10_500).Should().BeFalse();

        _cadence.Active(now: 10_500 + BarCadence.ActiveMs - 1).Should().BeTrue();
    }

    [TestMethod]
    public void A_busy_desktop_stays_fast_without_events()
    {
        _cadence.Next(now: 10_000, busy: true, away: false, wallSecond: 0, wallMillisecond: 0).Should().Be(BarCadence.FastMs);
    }

    [TestMethod]
    public void An_empty_room_turns_every_five_seconds()
    {
        _cadence.Next(now: 10_000, busy: false, away: true, wallSecond: 10, wallMillisecond: 400).Should().Be(BarCadence.AwayMs);
    }

    [TestMethod]
    public void An_empty_room_still_turns_just_after_the_wall_clock_minute()
    {
        _cadence.Next(now: 10_000, busy: false, away: true, wallSecond: 57, wallMillisecond: 0).Should().Be(3000 + BarCadence.SecondSkewMs);
        _cadence.Next(now: 10_000, busy: false, away: true, wallSecond: 59, wallMillisecond: 990).Should().Be(10 + BarCadence.SecondSkewMs);
        _cadence.Next(now: 10_000, busy: false, away: true, wallSecond: 0, wallMillisecond: 10).Should().Be(BarCadence.AwayMs);
    }

    [TestMethod]
    public void An_event_or_an_open_panel_keeps_the_fast_pace_in_an_empty_room()
    {
        _cadence.Next(now: 10_000, busy: true, away: true, wallSecond: 10, wallMillisecond: 0).Should().Be(BarCadence.FastMs);

        _cadence.Activity(now: 10_000);

        _cadence.Next(now: 10_000, busy: false, away: true, wallSecond: 10, wallMillisecond: 0).Should().Be(BarCadence.FastMs);
        _cadence.Next(now: 10_000 + BarCadence.ActiveMs, busy: false, away: true, wallSecond: 10, wallMillisecond: 0).Should().Be(BarCadence.AwayMs);
    }

    [TestMethod]
    public void The_timer_keeps_a_wait_that_lands_a_little_late_and_never_one_that_lands_early()
    {
        BarCadence.Keeps(current: 1000, next: 1000).Should().BeTrue();
        BarCadence.Keeps(current: 1000, next: 1000 - BarCadence.SlackMs).Should().BeTrue();
        BarCadence.Keeps(current: 1000, next: 1000 - BarCadence.SlackMs - 1).Should().BeFalse();
        BarCadence.Keeps(current: 1000, next: 1001).Should().BeFalse();
        BarCadence.Keeps(current: BarCadence.AwayMs, next: BarCadence.FastMs).Should().BeFalse();
    }
}
