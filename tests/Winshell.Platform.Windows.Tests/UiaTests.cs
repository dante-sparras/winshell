using Winshell.Platform.Windows.Accessibility;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Platform.Windows.Tests;

public class UiaTests
{
    private const string Text = "Hello, big world\nsecond line";

    [Theory]
    [InlineData(TextUnits.Character, 0, 0, 1)]
    [InlineData(TextUnits.Word, 2, 0, 5)]
    [InlineData(TextUnits.Word, 5, 5, 7)]
    [InlineData(TextUnits.Word, 8, 7, 11)]
    [InlineData(TextUnits.Line, 20, 17, 28)]
    [InlineData(TextUnits.Paragraph, 3, 0, 17)]
    [InlineData(TextUnits.Document, 9, 0, 28)]
    [InlineData(TextUnits.Page, 9, 0, 28)]
    public void A_range_expands_to_the_unit_its_start_is_in(int unit, int start, int expectedStart, int expectedEnd) =>
        Assert.Equal((expectedStart, expectedEnd), TextUnits.Enclose(Text, start, unit));

    [Fact]
    public void Moving_a_word_range_walks_the_words_and_stops_at_the_ends()
    {
        Assert.Equal((7, 11, 2), TextUnits.Move(Text, 0, 5, TextUnits.Word, 2));
        Assert.Equal((0, 5, -3), TextUnits.Move(Text, 11, 17, TextUnits.Word, -5));
        // A one-word range cannot move past the last word; an empty one can reach the very end.
        Assert.Equal((24, 28, 0), TextUnits.Move(Text, 24, 28, TextUnits.Word, 1));
        Assert.Equal((28, 28, 1), TextUnits.Move(Text, 24, 24, TextUnits.Word, 1));
    }

    [Fact]
    public void Moving_an_endpoint_counts_the_boundaries_it_crosses()
    {
        Assert.Equal((11, 3), TextUnits.MoveEndpoint(Text, 0, TextUnits.Word, 3));
        Assert.Equal((Text.Length, 2), TextUnits.MoveEndpoint(Text, 0, TextUnits.Line, 5));
        Assert.Equal((2, -1), TextUnits.MoveEndpoint(Text, 3, TextUnits.Character, -1));
        Assert.Equal((0, -1), TextUnits.MoveEndpoint(Text, 3, TextUnits.Document, -4));
    }

    [Fact]
    public void Units_do_not_split_a_surrogate_pair()
    {
        var text = "a😀b";
        Assert.Equal((1, 3), TextUnits.Enclose(text, 1, TextUnits.Character));
        Assert.Equal(3, TextUnits.NextStart(text, 1, TextUnits.Character));
        Assert.Equal(1, TextUnits.PreviousStart(text, 3, TextUnits.Character));
    }

    [Fact]
    public void A_character_is_one_grapheme_cluster()
    {
        var accented = "e\u0301x";
        Assert.Equal((0, 2), TextUnits.Enclose(accented, 1, TextUnits.Character));
        Assert.Equal(2, TextUnits.NextStart(accented, 0, TextUnits.Character));
        Assert.Equal(0, TextUnits.PreviousStart(accented, 2, TextUnits.Character));

        // A Thai vowel mark stays on its base. The family emoji is one ZWJ sequence.
        var thai = "ก้a";
        Assert.Equal((0, 2), TextUnits.Enclose(thai, 1, TextUnits.Character));
        var family = "👩‍👩‍👧";
        Assert.Equal((0, family.Length), TextUnits.Enclose(family, 4, TextUnits.Character));
        Assert.Equal(family.Length, TextUnits.NextStart(family, 0, TextUnits.Character));
    }

    [Fact]
    public void Each_ideograph_is_its_own_word()
    {
        var text = "中国人";
        Assert.Equal((0, 1), TextUnits.Enclose(text, 0, TextUnits.Word));
        Assert.Equal((1, 2), TextUnits.Enclose(text, 1, TextUnits.Word));
        Assert.Equal((2, 3, 2), TextUnits.Move(text, 0, 1, TextUnits.Word, 2));

        // The ideograph keeps its trailing spaces, and it does not stick to the Latin word beside it.
        Assert.Equal((0, 2), TextUnits.Enclose("中 国", 0, TextUnits.Word));
        Assert.Equal((2, 3), TextUnits.Enclose("中 国", 2, TextUnits.Word));
        Assert.Equal((0, 5), TextUnits.Enclose("Hello中", 0, TextUnits.Word));
        Assert.Equal((5, 6), TextUnits.Enclose("Hello中", 5, TextUnits.Word));
        Assert.Equal((0, 1), TextUnits.Enclose("中,", 0, TextUnits.Word));
        Assert.Equal((1, 2), TextUnits.Enclose("中,", 1, TextUnits.Word));

        // A supplementary-plane ideograph is one character and one word.
        var extended = "\U00020000\U00020001";
        Assert.Equal((0, 2), TextUnits.Enclose(extended, 1, TextUnits.Character));
        Assert.Equal((0, 2), TextUnits.Enclose(extended, 0, TextUnits.Word));
        Assert.Equal((2, 4), TextUnits.Enclose(extended, 2, TextUnits.Word));
    }

    [Theory]
    [InlineData(Role.Button, 50000)]
    [InlineData(Role.Switch, 50000)]
    [InlineData(Role.CheckBox, 50002)]
    [InlineData(Role.EditableText, 50004)]
    [InlineData(Role.StaticText, 50020)]
    [InlineData(Role.List, 50008)]
    [InlineData(Role.ListItem, 50007)]
    [InlineData(Role.PopupMenu, 50009)]
    [InlineData(Role.PageTabList, 50018)]
    [InlineData(Role.PageTab, 50019)]
    [InlineData(Role.Slider, 50015)]
    [InlineData(Role.Dialog, 50032)]
    [InlineData(Role.Pane, 50033)]
    [InlineData(Role.NoRole, 50025)]
    public void Roles_map_to_control_types(Role role, int controlType) => Assert.Equal(controlType, UiaElement.ControlType(role));
}
