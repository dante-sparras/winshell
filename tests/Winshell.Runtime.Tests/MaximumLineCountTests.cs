using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class MaximumLineCountTests
{
    private static readonly FontSpec Font = new("Segoe UI Variable", 14, 400, false, false, false, 0, 0);

    [Fact]
    public void A_line_cap_without_elide_drops_the_extra_lines()
    {
        using var full = Build(0, TextLayout.Elide.None);
        using var capped = Build(2, TextLayout.Elide.None);

        Assert.True(full.Lines.Count > 2, $"uncapped lines: {full.Lines.Count}");
        Assert.Equal(2, capped.Lines.Count);
        Assert.True(capped.Truncated);
        Assert.True(capped.Height < full.Height - 1);
    }

    [Fact]
    public void A_line_cap_with_elide_still_stops_at_the_cap()
    {
        using var capped = Build(2, TextLayout.Elide.Right);

        Assert.Equal(2, capped.Lines.Count);
        Assert.True(capped.Truncated);
    }

    private static TextLayout Build(int maxLines, TextLayout.Elide elide) =>
        TextLayout.Build(
            [new TextSpan("one two three four five six seven eight", Font)],
            48,
            TextLayout.Wrap.WordWrap,
            elide,
            maxLines,
            1,
            true);
}
