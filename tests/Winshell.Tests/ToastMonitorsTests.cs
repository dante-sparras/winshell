using FluentAssertions;

namespace Winshell.Tests;

/// <summary>Toasts are one list, drawn on every monitor.</summary>
[TestClass]
public sealed class ToastMonitorsTests
{
    [TestMethod]
    public void A_toast_is_drawn_on_every_monitor()
    {
        ToastMonitors.Draws(covered: [false, false], stays: false).Should().Equal(true, true);
        ToastMonitors.Draws(covered: [false], stays: false).Should().Equal(true);
    }

    [TestMethod]
    public void A_monitor_a_window_covers_draws_no_new_toast_and_the_others_do()
    {
        ToastMonitors.Draws(covered: [true, false], stays: false).Should().Equal(false, true);
    }

    [TestMethod]
    public void A_toast_that_stays_is_drawn_over_a_window_that_covers_its_monitor()
    {
        ToastMonitors.Draws(covered: [true, true], stays: true).Should().Equal(true, true);
    }

    [TestMethod]
    public void A_toast_no_monitor_draws_is_held_back()
    {
        ToastMonitors.Shown(ToastMonitors.Draws(covered: [true, true], stays: false)).Should().BeFalse();
        ToastMonitors.Shown(ToastMonitors.Draws(covered: [true], stays: false)).Should().BeFalse("one covered monitor holds a toast back");
        ToastMonitors.Shown(ToastMonitors.Draws(covered: [true, false], stays: false)).Should().BeTrue();
        ToastMonitors.Shown(ToastMonitors.Draws(covered: [], stays: true)).Should().BeFalse("there is no Bar to draw it against");
    }

    [TestMethod]
    public void A_stack_that_fits_every_monitor_loses_no_toast()
    {
        ToastColumn[] columns = [new([100, 100], Room: 1000, Gap: 8), new([150, 150], Room: 1500, Gap: 12)];

        ToastMonitors.Overflowing(columns, stays: [false, false]).Should().Be(-1);
    }

    [TestMethod]
    public void A_stack_too_tall_for_one_monitor_loses_its_oldest_toast_on_all()
    {
        // The second monitor is the short one: three toasts of 300 need 932 with the gaps, and it has 800.
        ToastColumn[] columns = [new([300, 300, 300], Room: 2000, Gap: 8), new([300, 300, 300], Room: 800, Gap: 8)];

        ToastMonitors.Overflowing(columns, stays: [false, false, false]).Should().Be(2);
    }

    [TestMethod]
    public void A_toast_that_stays_is_not_the_one_that_leaves()
    {
        ToastColumn[] columns = [new([300, 300, 300], Room: 800, Gap: 8)];

        ToastMonitors.Overflowing(columns, stays: [false, false, true]).Should().Be(1);
    }

    [TestMethod]
    public void A_toast_a_monitor_does_not_draw_takes_no_room_there()
    {
        // The middle toast arrived while a window covered this monitor, so it has no height here, and the other two fit.
        ToastColumn[] columns = [new([300, 0, 300], Room: 700, Gap: 8)];

        ToastMonitors.Overflowing(columns, stays: [false, false, false]).Should().Be(-1);

        // With all three drawn they do not, and the answer indexes the whole list, not the monitor's part of it.
        ToastMonitors.Overflowing([new([300, 0, 300, 300], Room: 700, Gap: 8)], stays: [false, false, false, false]).Should().Be(3);
    }
}
