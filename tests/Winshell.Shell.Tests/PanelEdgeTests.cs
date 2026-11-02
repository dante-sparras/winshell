namespace Winshell.ShellTests;

public sealed class PanelEdgeTests
{
    [Fact]
    public async Task A_placement_that_arrives_after_a_panel_is_gone_reserves_no_edge()
    {
        var holds = await ShellFixture.On(_ =>
        {
            var panel = new PanelWindow
            {
                Anchors = Edges.Top | Edges.Left | Edges.Right,
                Height = 8,
                ExclusiveZone = 8,
            };
            panel.Visible = true;
            var host = panel.Host!;
            panel.Dispose();

            // Explorer tells a bar when another bar moves, and that notice can land on the loop after the bar is gone.
            host.PlacePanel();

            return host.HoldsEdge;
        });

        Assert.False(holds);
    }
}
