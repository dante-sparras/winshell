using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class PanelPressTests
{
    [TestMethod]
    public void A_second_event_does_not_undo_the_click()
    {
        var press = new PanelPress();
        press.Begin();

        press.Act(open: false).Should().Be(PanelPressAction.Open);
        press.Act(open: true).Should().Be(PanelPressAction.None);
        press.End();
        press.Act(open: false).Should().Be(PanelPressAction.None);
    }

    [TestMethod]
    public void The_next_click_toggles_the_other_way()
    {
        var press = new PanelPress();

        press.Begin();
        press.Act(open: false).Should().Be(PanelPressAction.Open);
        press.End();

        press.Begin();
        press.Act(open: true).Should().Be(PanelPressAction.Close);
        press.End();

        press.Begin();
        press.Act(open: false).Should().Be(PanelPressAction.Open);
    }

    [TestMethod]
    public void A_repeat_down_stays_on_the_same_click()
    {
        var press = new PanelPress();
        press.Begin();
        press.Act(open: true).Should().Be(PanelPressAction.Close);

        press.Begin();
        press.Act(open: false).Should().Be(PanelPressAction.None);
    }
}
