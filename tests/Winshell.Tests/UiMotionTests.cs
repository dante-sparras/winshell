using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class UiMotionTests
{
    [TestMethod]
    public void The_durations_are_the_Windows_control_durations()
    {
        UiMotion.Faster.Should().Be(TimeSpan.FromMilliseconds(83));
        UiMotion.Fast.Should().Be(TimeSpan.FromMilliseconds(167));
        UiMotion.Normal.Should().Be(TimeSpan.FromMilliseconds(250));
        UiMotion.Slow.Should().Be(TimeSpan.FromMilliseconds(333));
    }

    [TestMethod]
    public void Animation_effects_off_makes_every_change_land_at_once()
    {
        UiMotion.For(UiMotion.Slow, animations: false).Should().Be(TimeSpan.Zero);
        UiMotion.For(UiMotion.Slow, animations: true).Should().Be(UiMotion.Slow);
    }

    [TestMethod]
    public void A_curve_starts_at_zero_and_ends_at_one()
    {
        MotionCurve.Decelerate.Progress(0).Should().Be(0);
        MotionCurve.Decelerate.Progress(1).Should().Be(1);
        MotionCurve.EaseOut.Progress(-1).Should().Be(0);
        MotionCurve.EaseOut.Progress(2).Should().Be(1);
    }

    [TestMethod]
    public void Ease_out_covers_most_of_a_turn_by_halfway()
    {
        MotionCurve.EaseOut.Progress(0.5).Should().BeApproximately(0.87, 0.02);
    }

    [TestMethod]
    public void A_linear_curve_follows_time()
    {
        var linear = new MotionCurve(0, 0, 1, 1);

        linear.Progress(0.25).Should().BeApproximately(0.25, 0.001);
        linear.Progress(0.75).Should().BeApproximately(0.75, 0.001);
    }
}
