using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class TextScrollTests
{
    [TestMethod]
    public void A_line_that_fits_sits_still()
    {
        TextScroll.For(120, 200, TextScroll.Gap, TextScroll.Speed).Should().Be(TextScroll.Still);
        TextScroll.For(200, 200, TextScroll.Gap, TextScroll.Speed).Should().Be(TextScroll.Still);
        TextScroll.For(400, 0, TextScroll.Gap, TextScroll.Speed).Should().Be(TextScroll.Still);
        TextScroll.For(400, 200, TextScroll.Gap, 0).Should().Be(TextScroll.Still);
        TextScroll.For(double.NaN, 200, TextScroll.Gap, TextScroll.Speed).Should().Be(TextScroll.Still);
        TextScroll.For(double.PositiveInfinity, 200, TextScroll.Gap, TextScroll.Speed).Should().Be(TextScroll.Still);
    }

    [TestMethod]
    public void A_line_that_does_not_fit_travels_its_own_width_and_the_gap()
    {
        var scroll = TextScroll.For(300, 200, gap: 40, speed: 36);

        scroll.Runs.Should().BeTrue();
        scroll.Travel.Should().Be(340);
        // TimeSpan rounds to the nearest tick, so the seconds are not the raw quotient.
        scroll.Duration.Should().Be(TimeSpan.FromSeconds(340d / 36d));
    }

    [TestMethod]
    public void A_longer_line_takes_longer_at_the_same_speed()
    {
        var scroll = TextScroll.For(300, 200, TextScroll.Gap, TextScroll.Speed);
        var longer = TextScroll.For(600, 200, TextScroll.Gap, TextScroll.Speed);

        longer.Travel.Should().Be(600 + TextScroll.Gap);
        (longer.Duration.TotalSeconds / scroll.Duration.TotalSeconds).Should().BeApproximately(longer.Travel / scroll.Travel, 1e-9);
    }

    [TestMethod]
    public void A_negative_gap_does_not_pull_the_copy_back_over_the_line()
    {
        var scroll = TextScroll.For(300, 200, gap: -10, speed: TextScroll.Speed);

        scroll.Travel.Should().Be(300);
    }

    [TestMethod]
    public void A_fixed_text_width_changes_the_face_mark()
    {
        var face = new BarFace { Text = "Track", TextWidth = 200 };

        BarFaceMark.Of(face).Should().Be(BarFaceMark.Of(new BarFace { Text = "Track", TextWidth = 200 }));
        BarFaceMark.Of(face).Should().NotBe(BarFaceMark.Of(face with { TextWidth = 160 }));
        face.Same(new BarFace { Text = "Track", TextWidth = 200 }).Should().BeTrue();
        face.Same(face with { TextWidth = 160 }).Should().BeFalse();
    }
}
