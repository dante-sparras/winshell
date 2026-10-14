using FluentAssertions;

namespace Winshell.Tests;

/// <summary>A panel's column keeps every separator's line the same distance from what the rows beside it draw.</summary>
[TestClass]
public sealed class RowColumnTests
{
    private const double RowGap = 4;
    private const double SeparatorGap = 12;

    [TestMethod]
    public void Rows_sit_one_row_gap_apart()
    {
        var layout = Lay(Row(20), Row(30), Row(10));

        layout.Tops.Should().Equal(0, 24, 58);
        layout.Height.Should().Be(68);
    }

    [TestMethod]
    public void A_line_sits_one_separator_gap_from_the_rows_beside_it()
    {
        var layout = Lay(Row(20), Line(), Row(30));

        LineTop(layout, 1).Should().Be(20 + SeparatorGap);
        layout.Tops[2].Should().Be(LineTop(layout, 1) + 1 + SeparatorGap);
    }

    [TestMethod]
    public void A_margin_on_the_row_beside_a_line_adds_nothing_to_the_gap()
    {
        var plain = Lay(Row(20), Line(), Row(30));
        var padded = Lay(Row(20 + 8, bottom: 8), Line(), Row(30 + 8, top: 8));

        // What the row above draws ends at 20 in both, and what the row below draws starts 8 under its top.
        LineTop(padded, 1).Should().Be(LineTop(plain, 1));
        (padded.Tops[2] + 8).Should().Be(plain.Tops[2]);
    }

    [TestMethod]
    public void A_line_that_leads_a_group_keeps_the_same_gap()
    {
        // A group that starts with a line: the line's own margin is inside the group's height.
        var group = new RowBox(SeparatorGap + 1 + SeparatorGap + 16, new RowEdge(SeparatorGap, true), new RowEdge(0, false));

        var layout = Lay(Row(20 + 8, bottom: 8), group);

        (layout.Tops[1] + SeparatorGap).Should().Be(20 + SeparatorGap);
    }

    [TestMethod]
    public void Two_lines_sit_one_separator_gap_apart()
    {
        var layout = Lay(Line(), Line());

        (LineTop(layout, 1) - (LineTop(layout, 0) + 1)).Should().Be(SeparatorGap);
    }

    [TestMethod]
    public void A_row_that_draws_nothing_takes_no_room_and_no_gap()
    {
        var with = Lay(Row(20), Row(0), Line(), Row(30));
        var without = Lay(Row(20), Line(), Row(30));

        with.Tops[2].Should().Be(without.Tops[1]);
        with.Height.Should().Be(without.Height);
    }

    [TestMethod]
    public void No_rows_have_no_height()
    {
        Lay().Height.Should().Be(0);
    }

    private static RowLayout Lay(params RowBox[] rows) => RowColumn.Lay(rows, RowGap, SeparatorGap);

    private static RowBox Row(double height, double top = 0, double bottom = 0) => new(height, new RowEdge(top, false), new RowEdge(bottom, false));

    /// <summary>A separator as the Kit draws it: a line one pixel tall with the separator gap as its own margin.</summary>
    private static RowBox Line() => new(SeparatorGap + 1 + SeparatorGap, new RowEdge(SeparatorGap, true), new RowEdge(SeparatorGap, true));

    private static double LineTop(RowLayout layout, int index) => layout.Tops[index] + SeparatorGap;
}
