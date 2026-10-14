using FluentAssertions;

namespace Winshell.Tests;

/// <summary>The arrow keys move a selection over a Panel's controls, and Return presses the selected one.</summary>
[TestClass]
public sealed class PanelSelectionTests
{
    private static readonly PanelLine[] Power =
    [
        new("heading", "Power", Detail: "On mains power"),
        new("slider", "Brightness", "0.4", Command: "brightness", Minimum: 0, Maximum: 1),
        new("choice", "Power mode", Rows:
        [
            new("option", "Saver", Command: "mode:saver", Selected: false),
            new("option", "Balanced", Command: "mode:balanced", Selected: true),
            new("option", "Performance", Command: "mode:performance", Selected: false),
        ]),
        new("switch", "Show battery percentage on the bar", Command: "percent", Selected: false),
        new("button", "Sleep", Command: "sleep"),
    ];

    [TestMethod]
    public void Down_selects_the_first_control_a_click_could_press()
    {
        var selection = new PanelSelection();

        var stroke = selection.Press(PanelKey.Down, Power);

        stroke.Used.Should().BeTrue();
        stroke.Command.Should().BeNull();
        selection.Selected(Power)!.Command.Should().Be("brightness");
    }

    [TestMethod]
    public void Down_and_Up_move_between_controls_and_stop_at_the_ends()
    {
        PanelLine[] rows =
        [
            new("heading", "Clock"),
            new("button", "Previous month", Command: "month:-1"),
            new("text", "October"),
            new("button", "Next month", Command: "month:+1"),
            new("option", "Oslo", Command: "zone:oslo"),
        ];
        var selection = new PanelSelection();
        var seen = new List<string?>();

        foreach (var key in new[] { PanelKey.Down, PanelKey.Down, PanelKey.Down, PanelKey.Down, PanelKey.Up, PanelKey.Up, PanelKey.Up })
        {
            selection.Press(key, rows).Used.Should().BeTrue();
            seen.Add(selection.Selected(rows)?.Command);
        }

        seen.Should().Equal("month:-1", "month:+1", "zone:oslo", "zone:oslo", "month:+1", "month:-1", "month:-1");
    }

    [TestMethod]
    public void Up_with_nothing_selected_selects_the_last_control()
    {
        var selection = new PanelSelection();

        selection.Press(PanelKey.Up, Power);

        selection.Selected(Power)!.Command.Should().Be("sleep");
    }

    [TestMethod]
    public void A_panel_with_no_control_to_press_leaves_the_keys_alone()
    {
        PanelLine[] rows = [new("heading", "Weather"), new("text", "Set a place")];
        var selection = new PanelSelection();

        selection.Press(PanelKey.Down, rows).Used.Should().BeFalse();
        selection.Press(PanelKey.Enter, rows).Used.Should().BeFalse();
        selection.Selected(rows).Should().BeNull();
    }

    [TestMethod]
    public void Return_sends_the_command_a_click_on_the_selected_control_sends()
    {
        var selection = new PanelSelection();
        selection.Press(PanelKey.Up, Power);

        var stroke = selection.Press(PanelKey.Enter, Power);

        stroke.Should().Be(new PanelStroke(true, "sleep"));
    }

    [TestMethod]
    public void Return_with_nothing_selected_is_left_alone()
    {
        var selection = new PanelSelection();

        selection.Press(PanelKey.Enter, Power).Should().Be(default(PanelStroke));
        selection.Press(PanelKey.Left, Power).Used.Should().BeFalse();
        selection.Press(PanelKey.Right, Power).Used.Should().BeFalse();
        selection.Selected(Power).Should().BeNull();
    }

    [TestMethod]
    public void A_held_Return_presses_once()
    {
        var selection = new PanelSelection();
        selection.Press(PanelKey.Up, Power);

        selection.Press(PanelKey.Enter, Power, held: true).Should().Be(new PanelStroke(true));
    }

    [TestMethod]
    public void Return_on_a_switch_turns_it_the_other_way()
    {
        PanelLine[] off = [new("switch", "Wi-Fi", Command: "radio", Selected: false)];
        PanelLine[] on = [new("switch", "Wi-Fi", Command: "radio", Selected: true)];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, off);

        selection.Press(PanelKey.Enter, off).Should().Be(new PanelStroke(true, "radio", 1));
        selection.Press(PanelKey.Enter, on).Should().Be(new PanelStroke(true, "radio", 0));
    }

    [TestMethod]
    public void Left_and_Right_move_a_slider_a_twentieth_of_its_range()
    {
        PanelLine[] rows = [new("slider", "Output", "0.4", Command: "volume", Minimum: 0, Maximum: 1)];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, rows);

        selection.Press(PanelKey.Right, rows).Should().Be(new PanelStroke(true, "volume", 0.45));
        selection.Press(PanelKey.Left, rows).Should().Be(new PanelStroke(true, "volume", 0.35));
    }

    [TestMethod]
    public void A_slider_carries_its_range_as_numbers_and_stops_at_its_ends()
    {
        PanelLine[] high = [new("slider", "Warmth", "6400", Command: "warmth", Minimum: 2500, Maximum: 6500)];
        PanelLine[] low = [new("slider", "Warmth", "2500", Command: "warmth", Minimum: 2500, Maximum: 6500)];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, high);

        selection.Press(PanelKey.Right, high).Should().Be(new PanelStroke(true, "warmth", 6500));
        selection.Press(PanelKey.Left, high).Should().Be(new PanelStroke(true, "warmth", 6200));
        selection.Press(PanelKey.Left, low).Should().Be(new PanelStroke(true), "a slider at its end has nowhere to go, and the key is still its own");
    }

    [TestMethod]
    public void A_slider_that_carries_no_range_runs_from_zero_to_one_whatever_its_text_says()
    {
        PanelLine[] rows = [new("slider", "Level", "0.98", "20 to 80", "level")];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, rows);

        selection.Press(PanelKey.Right, rows).Should().Be(new PanelStroke(true, "level", 1));
    }

    [TestMethod]
    public void Left_and_Right_step_a_stepper_back_and_forward()
    {
        PanelLine[] rows = [new("stepper", "Warn at", "80%", Command: "warn")];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, rows);

        selection.Press(PanelKey.Left, rows).Should().Be(new PanelStroke(true, "warn", -1));
        selection.Press(PanelKey.Right, rows).Should().Be(new PanelStroke(true, "warn", 1));
    }

    [TestMethod]
    public void Return_on_a_slider_or_a_stepper_sends_nothing()
    {
        PanelLine[] rows = [new("slider", "Output", "0.4", Command: "volume", Minimum: 0, Maximum: 1), new("stepper", "Warn at", "80%", Command: "warn")];
        var selection = new PanelSelection();

        selection.Press(PanelKey.Down, rows);
        selection.Press(PanelKey.Enter, rows).Should().Be(new PanelStroke(true));
        selection.Press(PanelKey.Down, rows);
        selection.Press(PanelKey.Enter, rows).Should().Be(new PanelStroke(true));
    }

    [TestMethod]
    public void Left_and_Right_on_a_plain_control_are_used_and_send_nothing()
    {
        var selection = new PanelSelection();
        selection.Press(PanelKey.Up, Power);

        selection.Press(PanelKey.Left, Power).Should().Be(new PanelStroke(true));
        selection.Press(PanelKey.Right, Power).Should().Be(new PanelStroke(true));
        selection.Selected(Power)!.Command.Should().Be("sleep");
    }

    [TestMethod]
    public void Down_and_Up_take_a_row_of_chips_as_one_control_and_enter_it_at_the_chosen_chip()
    {
        var selection = new PanelSelection();
        var seen = new List<string?>();

        foreach (var key in new[] { PanelKey.Down, PanelKey.Down, PanelKey.Down, PanelKey.Up, PanelKey.Up })
        {
            selection.Press(key, Power);
            seen.Add(selection.Selected(Power)?.Command);
        }

        seen.Should().Equal("brightness", "mode:balanced", "percent", "mode:balanced", "brightness");
    }

    [TestMethod]
    public void Left_and_Right_move_along_a_row_of_chips_and_choose_none()
    {
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, Power);
        selection.Press(PanelKey.Down, Power);
        var seen = new List<string?>();

        foreach (var key in new[] { PanelKey.Right, PanelKey.Right, PanelKey.Left, PanelKey.Left, PanelKey.Left })
        {
            selection.Press(key, Power).Should().Be(new PanelStroke(true));
            seen.Add(selection.Selected(Power)?.Command);
        }

        seen.Should().Equal("mode:performance", "mode:performance", "mode:balanced", "mode:saver", "mode:saver");
        selection.Press(PanelKey.Enter, Power).Should().Be(new PanelStroke(true, "mode:saver"));
    }

    [TestMethod]
    public void A_tab_strip_with_no_chosen_tab_is_entered_at_its_first()
    {
        PanelLine[] rows =
        [
            new("tabs", Rows: [new("tab", "Usage", Command: "page:usage"), new("tab", "Settings", Command: "page:settings")]),
            new("button", "Refresh", Command: "refresh"),
        ];
        var selection = new PanelSelection();

        selection.Press(PanelKey.Up, rows);
        selection.Press(PanelKey.Up, rows);

        selection.Selected(rows)!.Command.Should().Be("page:usage");
    }

    [TestMethod]
    public void The_controls_inside_a_card_are_reached_one_by_one()
    {
        PanelLine[] rows =
        [
            new("heading", "Network", Rows: [new("button", "Share this network", Command: "qr"), new("switch", "Wi-Fi", Command: "radio", Selected: true)]),
            new("subscription", "Claude", Rows: [new("switch", "Claude enabled", Command: "enable:claude", Selected: true), new("stepper", "Warn at", "80%", Command: "warn:claude")]),
        ];
        var selection = new PanelSelection();
        var seen = new List<string?>();

        for (var i = 0; i < 4; i++)
        {
            selection.Press(PanelKey.Down, rows);
            seen.Add(selection.Selected(rows)?.Command);
        }

        seen.Should().Equal("qr", "radio", "enable:claude", "warn:claude");
    }

    [TestMethod]
    public void The_selection_follows_its_control_when_the_rows_move()
    {
        PanelLine[] before = [Network("Cafe"), Network("Home"), Network("Office")];
        PanelLine[] after = [Network("Airport"), Network("Office"), Network("Cafe"), Network("Home")];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, before);
        selection.Press(PanelKey.Down, before);

        selection.Selected(after)!.Command.Should().Be("net:Home");
        selection.At(after).Should().Be(3);
        selection.Press(PanelKey.Up, after);
        selection.Selected(after)!.Command.Should().Be("net:Cafe");
    }

    [TestMethod]
    public void The_selection_keeps_its_place_when_its_control_is_gone()
    {
        PanelLine[] before = [Network("Cafe"), Network("Home"), Network("Office")];
        PanelLine[] asked = [Network("Cafe"), new("option", "Home", Command: "ask:Home"), Network("Office")];
        PanelLine[] fewer = [Network("Cafe")];
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, before);
        selection.Press(PanelKey.Down, before);

        selection.Press(PanelKey.Enter, asked).Should().Be(new PanelStroke(true, "ask:Home"), "the row at that place sends another command now");
        selection.Selected(fewer)!.Command.Should().Be("net:Cafe");
        selection.Selected([]).Should().BeNull();
        selection.At(before).Should().Be(-1, "a Panel that showed no control selects none");
    }

    [TestMethod]
    public void Two_controls_that_send_one_command_are_both_reached()
    {
        PanelLine[] rows =
        [
            new("heading", "Weather", Rows: [new("button", "Change location", Command: "edit")]),
            new("option", "Celsius", Command: "unit:c"),
            new("option", "Change location", Command: "edit"),
        ];
        var selection = new PanelSelection();
        var places = new List<int>();

        foreach (var key in new[] { PanelKey.Down, PanelKey.Down, PanelKey.Down, PanelKey.Down, PanelKey.Up })
        {
            selection.Press(key, rows);
            places.Add(selection.At(rows));
        }

        places.Should().Equal(0, 1, 2, 2, 1);
    }

    [TestMethod]
    public void The_selection_starts_from_the_control_the_pointer_was_on()
    {
        var selection = new PanelSelection();

        selection.Select(Power, 4);
        selection.Selected(Power)!.Command.Should().Be("percent");
        selection.Select(Power, 9);
        selection.Press(PanelKey.Down, Power);

        selection.Selected(Power)!.Command.Should().Be("sleep");
    }

    [TestMethod]
    public void A_cleared_selection_selects_nothing()
    {
        var selection = new PanelSelection();
        selection.Press(PanelKey.Down, Power);

        selection.Clear();

        selection.Selected(Power).Should().BeNull();
        selection.At(Power).Should().Be(-1);
    }

    [TestMethod]
    public void A_selection_is_active_from_its_first_move_until_it_is_cleared()
    {
        var selection = new PanelSelection();
        selection.Active.Should().BeFalse();

        selection.Press(PanelKey.Enter, Power);
        selection.Active.Should().BeFalse("Return with nothing selected selects nothing");
        selection.Press(PanelKey.Down, Power);
        selection.Active.Should().BeTrue();
        selection.Clear();

        selection.Active.Should().BeFalse();
    }

    [TestMethod]
    [DataRow("Up", PanelKey.Up)]
    [DataRow("down", PanelKey.Down)]
    [DataRow("Left", PanelKey.Left)]
    [DataRow("RIGHT", PanelKey.Right)]
    [DataRow("Enter", PanelKey.Enter)]
    [DataRow(" Return ", PanelKey.Enter)]
    public void A_key_is_read_by_its_name(string text, PanelKey key)
    {
        PanelSelection.TryKey(text, out var read).Should().BeTrue();
        read.Should().Be(key);
    }

    [TestMethod]
    [DataRow("Ctrl+K")]
    [DataRow("Shift+Down")]
    [DataRow("Tab")]
    [DataRow("1")]
    [DataRow("")]
    [DataRow(null)]
    public void Any_other_text_is_not_a_key_of_the_selection(string? text)
    {
        PanelSelection.TryKey(text, out _).Should().BeFalse();
    }

    private static PanelLine Network(string name) => new("option", name, Command: "net:" + name);
}
