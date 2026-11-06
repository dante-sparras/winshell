using Winshell.Native;

namespace Winshell.ShellTests;

/// <summary>Which edge a panel reserves, what it asks Explorer for, and where it lays out in what Explorer grants.</summary>
public class AppBarTests
{
    // A 1920×1080 monitor and a 48 px bar.
    private static readonly RECT Monitor = new(0, 0, 1920, 1080);

    // A 2560×1440 monitor below and to the right of it, so an edge's coordinates are never its depth.
    private static readonly RECT Second = new(2560, 1080, 5120, 2520);

    [Theory]
    [InlineData(Edges.Top | Edges.Left | Edges.Right, Edges.Top)]
    [InlineData(Edges.Bottom | Edges.Left | Edges.Right, Edges.Bottom)]
    [InlineData(Edges.Left | Edges.Top | Edges.Bottom, Edges.Left)]
    [InlineData(Edges.Right | Edges.Top | Edges.Bottom, Edges.Right)]
    [InlineData(Edges.Bottom, Edges.Bottom)]
    public void A_panel_along_one_edge_reserves_that_edge(Edges anchors, Edges edge)
    {
        Assert.Equal(edge, AppBar.EdgeFor(anchors));
    }

    [Theory]
    [InlineData(Edges.None)]
    [InlineData(Edges.Top | Edges.Left)]
    [InlineData(Edges.Left | Edges.Right)]
    [InlineData(Edges.Top | Edges.Bottom | Edges.Left | Edges.Right)]
    public void A_panel_in_a_corner_or_across_the_screen_reserves_no_edge(Edges anchors)
    {
        Assert.Null(AppBar.EdgeFor(anchors));
    }

    [Theory]
    [InlineData(Edges.Top, 0, 0, 1920, 48)]
    [InlineData(Edges.Bottom, 0, 1032, 1920, 1080)]
    [InlineData(Edges.Left, 0, 0, 48, 1080)]
    [InlineData(Edges.Right, 1872, 0, 1920, 1080)]
    public void A_bar_asks_for_a_strip_as_deep_as_its_size_along_its_edge(Edges edge, int left, int top, int right,
        int bottom)
    {
        Assert.Equal(new RECT(left, top, right, bottom), AppBar.Strip(edge, 48, Monitor));
    }

    // Explorer grants a strip beside the taskbar, which holds 48 px along the top or the bottom.
    [Theory]
    [InlineData(Edges.Top, 0, 48, 1920, 96, 0, 48, 1920, 1080)]
    [InlineData(Edges.Bottom, 0, 984, 1920, 1032, 0, 0, 1920, 1032)]
    [InlineData(Edges.Left, 0, 48, 48, 1080, 0, 48, 1920, 1080)]
    [InlineData(Edges.Right, 1872, 0, 1920, 1032, 0, 0, 1920, 1032)]
    public void A_bar_lays_out_from_where_its_grant_starts_and_along_it(
        Edges edge, int grantLeft, int grantTop, int grantRight, int grantBottom, int left, int top, int right,
        int bottom)
    {
        Assert.Equal(new RECT(left, top, right, bottom),
            AppBar.Area(edge, new RECT(grantLeft, grantTop, grantRight, grantBottom), Monitor));
    }

    [Theory]
    [InlineData(Edges.Top, 2560, 1080, 5120, 1128)]
    [InlineData(Edges.Bottom, 2560, 2472, 5120, 2520)]
    [InlineData(Edges.Left, 2560, 1080, 2608, 2520)]
    [InlineData(Edges.Right, 5072, 1080, 5120, 2520)]
    public void A_bar_on_a_monitor_away_from_the_origin_asks_for_a_strip_of_that_monitor(Edges edge, int left, int top,
        int right, int bottom)
    {
        Assert.Equal(new RECT(left, top, right, bottom), AppBar.Strip(edge, 48, Second));
    }

    // Explorer grants a strip shortened along its edge by a taskbar on a neighboring edge, and the bar keeps out of it.
    [Theory]
    [InlineData(Edges.Top, 2608, 1080, 5120, 1128, 2608, 1080, 5120, 2520)]
    [InlineData(Edges.Bottom, 2560, 2472, 5072, 2520, 2560, 1080, 5072, 2520)]
    [InlineData(Edges.Left, 2560, 1080, 2608, 2472, 2560, 1080, 5120, 2472)]
    [InlineData(Edges.Right, 5072, 1128, 5120, 2520, 2560, 1128, 5120, 2520)]
    public void A_bar_lays_out_only_as_long_as_its_grant_along_its_edge(
        Edges edge, int grantLeft, int grantTop, int grantRight, int grantBottom, int left, int top, int right,
        int bottom)
    {
        Assert.Equal(new RECT(left, top, right, bottom),
            AppBar.Area(edge, new RECT(grantLeft, grantTop, grantRight, grantBottom), Second));
    }
}
