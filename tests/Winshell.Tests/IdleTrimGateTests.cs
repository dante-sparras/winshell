using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

[TestClass]
public sealed class IdleTrimGateTests
{
    private const long Megabyte = 1024 * 1024;
    private readonly IdleTrimGate _gate = new();

    [TestMethod]
    public void The_first_trim_waits_for_the_person_to_be_away()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: IdleTrimGate.IdleSeconds - 1, busy: false, allocatedBytes: 0).Should().BeFalse();

        _gate.ShouldTrim(now: 1000, idleSeconds: IdleTrimGate.IdleSeconds, busy: false, allocatedBytes: 0).Should().BeTrue();
    }

    [TestMethod]
    public void Nothing_is_trimmed_while_something_is_on_screen()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: 600, busy: true, allocatedBytes: 0).Should().BeFalse();
    }

    [TestMethod]
    public void A_second_trim_waits_for_the_gap()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: 60, busy: false, allocatedBytes: 0).Should().BeTrue();

        _gate.ShouldTrim(now: IdleTrimGate.GapMs - 1, idleSeconds: 60, busy: false, allocatedBytes: 100 * Megabyte).Should().BeFalse();
        _gate.ShouldTrim(now: IdleTrimGate.GapMs, idleSeconds: 60, busy: false, allocatedBytes: 100 * Megabyte).Should().BeTrue();
    }

    [TestMethod]
    public void A_second_trim_waits_for_enough_new_allocation()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: 60, busy: false, allocatedBytes: 50 * Megabyte).Should().BeTrue();

        _gate.ShouldTrim(now: 10 * IdleTrimGate.GapMs, idleSeconds: 60, busy: false, allocatedBytes: (50 * Megabyte) + IdleTrimGate.WorthBytes - 1).Should().BeFalse();
        _gate.ShouldTrim(now: 10 * IdleTrimGate.GapMs, idleSeconds: 60, busy: false, allocatedBytes: (50 * Megabyte) + IdleTrimGate.WorthBytes).Should().BeTrue();
    }

    [TestMethod]
    public void Input_between_trims_does_not_reset_what_was_counted()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: 60, busy: false, allocatedBytes: 0).Should().BeTrue();
        _gate.ShouldTrim(now: IdleTrimGate.GapMs, idleSeconds: 0, busy: false, allocatedBytes: 100 * Megabyte).Should().BeFalse();

        _gate.ShouldTrim(now: IdleTrimGate.GapMs + 30_000, idleSeconds: 30, busy: false, allocatedBytes: 100 * Megabyte).Should().BeTrue();
    }

    [TestMethod]
    public void Each_trim_with_nobody_back_waits_twice_as_long_as_the_one_before()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: 60, busy: false, allocatedBytes: 0).Should().BeTrue();
        var second = IdleTrimGate.GapMs;
        _gate.ShouldTrim(second, idleSeconds: 180, busy: false, allocatedBytes: 100 * Megabyte).Should().BeTrue();

        var third = second + (2 * IdleTrimGate.GapMs);
        _gate.ShouldTrim(third - 1, idleSeconds: 400, busy: false, allocatedBytes: 200 * Megabyte).Should().BeFalse();
        _gate.ShouldTrim(third, idleSeconds: 400, busy: false, allocatedBytes: 200 * Megabyte).Should().BeTrue();

        var fourth = third + (4 * IdleTrimGate.GapMs);
        _gate.ShouldTrim(fourth - 1, idleSeconds: 900, busy: false, allocatedBytes: 300 * Megabyte).Should().BeFalse();
        _gate.ShouldTrim(fourth, idleSeconds: 900, busy: false, allocatedBytes: 300 * Megabyte).Should().BeTrue();
    }

    [TestMethod]
    public void The_wait_between_trims_stops_growing_at_the_longest_gap()
    {
        var now = 0L;
        var allocated = 0L;
        _gate.ShouldTrim(now, idleSeconds: 60, busy: false, allocated).Should().BeTrue();
        for (var trim = 0; trim < 12; trim++)
        {
            now += IdleTrimGate.LongestGapMs;
            allocated += 100 * Megabyte;
            _gate.ShouldTrim(now, idleSeconds: 60, busy: false, allocated).Should().BeTrue();
        }

        allocated += 100 * Megabyte;
        _gate.ShouldTrim(now + IdleTrimGate.LongestGapMs - 1, idleSeconds: 60, busy: false, allocated).Should().BeFalse();
        _gate.ShouldTrim(now + IdleTrimGate.LongestGapMs, idleSeconds: 60, busy: false, allocated).Should().BeTrue();
    }

    [TestMethod]
    public void The_person_coming_back_puts_the_wait_at_the_short_gap_again()
    {
        _gate.ShouldTrim(now: 0, idleSeconds: 60, busy: false, allocatedBytes: 0).Should().BeTrue();
        _gate.ShouldTrim(IdleTrimGate.GapMs, idleSeconds: 180, busy: false, allocatedBytes: 100 * Megabyte).Should().BeTrue();
        var back = IdleTrimGate.GapMs + 5000;
        _gate.ShouldTrim(back, idleSeconds: 0, busy: false, allocatedBytes: 100 * Megabyte).Should().BeFalse();

        var next = (2 * IdleTrimGate.GapMs) + 1000;
        _gate.ShouldTrim(next, idleSeconds: 30, busy: false, allocatedBytes: 200 * Megabyte).Should().BeTrue();
        _gate.ShouldTrim(next + IdleTrimGate.GapMs, idleSeconds: 150, busy: false, allocatedBytes: 300 * Megabyte).Should().BeTrue();
    }

    [TestMethod]
    public void A_trim_the_caller_ran_by_itself_counts_as_the_last_one()
    {
        _gate.Trimmed(now: 1000, allocatedBytes: 10 * Megabyte);

        _gate.ShouldTrim(now: 30_000, idleSeconds: 29, busy: false, allocatedBytes: 100 * Megabyte).Should().BeFalse();
        _gate.ShouldTrim(now: 1000 + IdleTrimGate.GapMs, idleSeconds: 120, busy: false, allocatedBytes: 100 * Megabyte).Should().BeTrue();
    }

    [TestMethod]
    public void A_sweep_runs_once_enough_was_allocated_and_enough_time_passed_since_the_last_collection()
    {
        _gate.ShouldSweep(now: 0, busy: false, allocatedBytes: 0, collections: 3).Should().BeFalse();

        _gate.ShouldSweep(IdleTrimGate.SweepGapMs - 1, busy: false, IdleTrimGate.SweepBytes, collections: 3).Should().BeFalse();
        _gate.ShouldSweep(IdleTrimGate.SweepGapMs, busy: false, IdleTrimGate.SweepBytes - 1, collections: 3).Should().BeFalse();
        _gate.ShouldSweep(IdleTrimGate.SweepGapMs, busy: false, IdleTrimGate.SweepBytes, collections: 3).Should().BeTrue();
    }

    [TestMethod]
    public void A_sweep_does_not_wait_for_the_person_to_be_away_but_waits_for_the_screen()
    {
        _gate.ShouldSweep(now: 0, busy: false, allocatedBytes: 0, collections: 0).Should().BeFalse();

        _gate.ShouldSweep(IdleTrimGate.SweepGapMs, busy: true, IdleTrimGate.SweepBytes, collections: 0).Should().BeFalse();
        _gate.ShouldSweep(IdleTrimGate.SweepGapMs + 200, busy: false, IdleTrimGate.SweepBytes, collections: 0).Should().BeTrue();
    }

    [TestMethod]
    public void A_collection_of_any_kind_starts_the_count_for_the_next_sweep_again()
    {
        _gate.ShouldSweep(now: 0, busy: false, allocatedBytes: 0, collections: 0).Should().BeFalse();

        // The collector ran by itself after 20 seconds and 3 MB.
        _gate.ShouldSweep(now: 20_000, busy: false, allocatedBytes: 3 * Megabyte, collections: 1).Should().BeFalse();
        _gate.ShouldSweep(now: 40_000, busy: false, allocatedBytes: 8 * Megabyte, collections: 1).Should().BeFalse();
        _gate.ShouldSweep(now: 20_000 + IdleTrimGate.SweepGapMs, busy: false, allocatedBytes: (3 * Megabyte) + IdleTrimGate.SweepBytes, collections: 1).Should().BeTrue();
    }

    [TestMethod]
    public void A_sweep_that_ran_is_not_asked_for_again_before_its_collection_is_counted()
    {
        _gate.ShouldSweep(now: 0, busy: false, allocatedBytes: 0, collections: 0).Should().BeFalse();
        _gate.ShouldSweep(IdleTrimGate.SweepGapMs, busy: false, IdleTrimGate.SweepBytes, collections: 0).Should().BeTrue();

        _gate.ShouldSweep(IdleTrimGate.SweepGapMs + 200, busy: false, 3 * IdleTrimGate.SweepBytes, collections: 0).Should().BeFalse();
    }
}
