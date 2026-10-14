using FluentAssertions;

namespace Winshell.Tests;

/// <summary>A Kit control prints itself for <c>winoma desktop</c>, and <c>winoma summon</c> presses it by its command.</summary>
[TestClass]
public sealed class PanelControlTests
{
    [TestMethod]
    public void A_summon_presses_the_control_with_that_command()
    {
        var pressed = new List<string>();
        var controls = new[]
        {
            new PanelControl("", () => new PanelLine("text", "Hello")),
            new PanelControl("month:+1", () => new PanelLine("button", "Next month", Command: "month:+1"), (_, _) => pressed.Add("next")),
            new PanelControl("volume", () => new PanelLine("slider", "Output", Command: "volume"), (value, _) => pressed.Add("volume " + value?.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        };

        PanelControl.Find(controls, "volume")!.Press(0.4, null);
        PanelControl.Find(controls, "month:+1")!.Press(null, null);

        pressed.Should().Equal("volume 0.4", "next");
    }

    [TestMethod]
    public void A_command_no_control_has_goes_to_the_plugin()
    {
        var controls = new[] { new PanelControl("month:+1", () => new PanelLine("button"), (_, _) => { }) };

        PanelControl.Find(controls, "zone:UTC").Should().BeNull();
        PanelControl.Find(controls, "").Should().BeNull();
    }

    [TestMethod]
    public void A_control_that_only_shows_cannot_be_pressed()
    {
        var label = new PanelControl("", () => new PanelLine("text", "Hi"));

        label.Pressable.Should().BeFalse();
        label.Describe().Label.Should().Be("Hi");
    }

    [TestMethod]
    public void A_pressable_control_needs_a_command()
    {
        var create = () => new PanelControl("", () => new PanelLine("button"), (_, _) => { });

        create.Should().Throw<ArgumentException>();
    }
}
