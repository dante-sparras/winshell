using FluentAssertions;

namespace Winshell.Tests;

/// <summary>A thing that fails turn after turn is written once in full, then counted.</summary>
[TestClass]
public sealed class FaultRunsTests
{
    [TestMethod]
    public void The_first_failure_is_written_in_full()
    {
        new FaultRuns().Failed("acme.usage", 1_000).Should().Be(new FaultNote(First: true, Count: 1));
    }

    [TestMethod]
    public void The_failures_that_follow_are_not_written()
    {
        var runs = new FaultRuns();
        runs.Failed("acme.usage", 1_000);

        runs.Failed("acme.usage", 1_200).Should().Be(new FaultNote(First: false, Count: 0));
    }

    [TestMethod]
    public void A_run_that_goes_on_is_counted_every_ten_minutes()
    {
        var runs = new FaultRuns();
        runs.Failed("acme.usage", 1_000);
        runs.Failed("acme.usage", 1_200);

        runs.Failed("acme.usage", 1_000 + FaultRuns.CountEveryMs).Should().Be(new FaultNote(First: false, Count: 3));
        runs.Failed("acme.usage", 1_200 + FaultRuns.CountEveryMs).Should().Be(new FaultNote(First: false, Count: 0));
    }

    [TestMethod]
    public void A_recovery_says_how_many_failed_and_ends_the_run()
    {
        var runs = new FaultRuns();
        runs.Failed("acme.usage", 1_000);
        runs.Failed("acme.usage", 1_200);

        runs.Recovered("acme.usage").Should().Be(2);
        runs.Recovered("acme.usage").Should().Be(0);
        runs.Failed("acme.usage", 1_400).First.Should().BeTrue();
    }

    [TestMethod]
    public void One_plugins_run_is_not_anothers()
    {
        var runs = new FaultRuns();
        runs.Failed("acme.usage", 1_000);

        runs.Failed("acme.clock", 1_100).First.Should().BeTrue();
    }
}
