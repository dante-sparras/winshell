using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class StripPanTests
{
    [TestMethod]
    public void A_dragged_strip_follows_the_pointer_and_stops_at_either_end()
    {
        StripPan.Dragged(startOffset: 100, pressedAt: 200, pointerAt: 150, extent: 1000, viewport: 400).Should().Be(150, "the pointer went 50 left, so the strip shows 50 further on");
        StripPan.Dragged(startOffset: 100, pressedAt: 200, pointerAt: 260, extent: 1000, viewport: 400).Should().Be(40);
        StripPan.Dragged(startOffset: 100, pressedAt: 200, pointerAt: 900, extent: 1000, viewport: 400).Should().Be(0);
        StripPan.Dragged(startOffset: 100, pressedAt: 200, pointerAt: -900, extent: 1000, viewport: 400).Should().Be(600);
        StripPan.Dragged(startOffset: 0, pressedAt: 200, pointerAt: 100, extent: 300, viewport: 400).Should().Be(0, "a strip that fits has nowhere to go");
    }

    [TestMethod]
    public void The_wheel_moves_a_strip_one_step_a_notch()
    {
        StripPan.Wheeled(offset: 100, delta: -120, horizontal: false, step: 80, extent: 1000, viewport: 400).Should().Be(180, "the wheel turned toward the person moves on");
        StripPan.Wheeled(offset: 100, delta: 120, horizontal: false, step: 80, extent: 1000, viewport: 400).Should().Be(20);
        StripPan.Wheeled(offset: 100, delta: 120, horizontal: true, step: 80, extent: 1000, viewport: 400).Should().Be(180, "a tilt to the right moves on");
        StripPan.Wheeled(offset: 100, delta: -30, horizontal: false, step: 80, extent: 1000, viewport: 400).Should().Be(120, "a touchpad sends parts of a notch");
        StripPan.Wheeled(offset: 580, delta: -120, horizontal: false, step: 80, extent: 1000, viewport: 400).Should().Be(600);
        StripPan.Wheeled(offset: 20, delta: 120, horizontal: false, step: 80, extent: 1000, viewport: 400).Should().Be(0);
    }
}
