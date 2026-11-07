using Winshell.Native;

namespace Winshell.ShellTests;

/// <summary>
/// Where a panel sits and which edge it holds, against a stand-in for the desktop. What a real window does with those
/// answers is in the Slow tests that need one.
/// </summary>
public class PanelEdgeDecisionTests
{
    private static readonly PanelMonitor Primary = new("primary", new RECT(0, 0, 1920, 1080),
        new RECT(0, 40, 1920, 1080), 1);

    private static readonly PanelMonitor Second =
        new("second", new RECT(2560, 1080, 5120, 2520), new RECT(2560, 1080, 5120, 2472), 1);

    private static readonly PanelMonitor Scaled =
        new("scaled", new RECT(0, 0, 1920, 1080), new RECT(0, 80, 1920, 1080), 2);

    /// <summary>A desktop that records the edge it was asked for and where the window went.</summary>
    private sealed class Desktop : IPanelDesktop
    {
        public bool Fullscreen { get; set; }

        public int Registers { get; private set; }

        public int Removes { get; private set; }

        public int Reserves { get; private set; }

        public RECT? Asked { get; private set; }

        public RECT? Moved { get; private set; }

        public bool? Topmost { get; private set; }

        public double Scale { get; private set; }

        public List<Action> Posted { get; } = [];

        public PanelMonitor? Find(string id)
        {
            if (id == Second.Id)
                return Second;
            return id == Scaled.Id ? Scaled : null;
        }

        public PanelMonitor? Primary()
        {
            return PanelEdgeDecisionTests.Primary;
        }

        public bool FullscreenOn(string screenId)
        {
            return Fullscreen && screenId == PanelEdgeDecisionTests.Primary.Id;
        }

        public IDisposable Explorer()
        {
            return new Noop();
        }

        public bool Register(nint window, uint callback)
        {
            Registers++;
            return true;
        }

        public RECT Query(nint window, AppBarEdge edge, RECT proposed)
        {
            return proposed;
        }

        public RECT Reserve(nint window, AppBarEdge edge, RECT desired)
        {
            Reserves++;
            Asked = desired;
            return desired;
        }

        public void Remove(nint window)
        {
            Removes++;
        }

        public void Move(RECT rect, bool topmost, double scale)
        {
            Moved = rect;
            Topmost = topmost;
            Scale = scale;
        }

        public void WorkAreaMayHaveChanged()
        {
        }

        public void Post(Action action)
        {
            Posted.Add(action);
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static PanelPlacement Panel(Edges anchors, string? screen = null, bool covers = false, bool above = true,
        double reserved = 48)
    {
        return new PanelPlacement(screen is null ? null : Screen(screen), anchors, 0, 0, 0, 0, 320, 48, covers,
            reserved, above, false);
    }

    private static ScreenInfo Screen(string id)
    {
        return new ScreenInfo(id, id, "", "", "", default, default, 1, 0, 0, 60, false);
    }

    [Fact]
    public void A_panel_reserves_a_strip_of_the_screen_it_names()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(1, desktop, () => { });

        Assert.True(edge.Place(Panel(Edges.Top | Edges.Left | Edges.Right, Second.Id), true, false));

        Assert.Equal(new RECT(2560, 1080, 5120, 1128), desktop.Asked);
        Assert.Equal(1, desktop.Registers);
        Assert.True(desktop.Topmost);
        edge.Remove();
    }

    [Fact]
    public void A_panel_moves_at_the_scale_of_the_monitor_it_is_on()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(8, desktop, () => { });

        edge.Place(Panel(Edges.Top | Edges.Left, Scaled.Id), true, false);

        // 320 by 48 logical pixels at scale 2, against the work area's top.
        Assert.Equal(2, desktop.Scale);
        Assert.Equal(new RECT(0, 80, 640, 176), desktop.Moved);
    }

    [Fact]
    public void A_panel_that_covers_its_screen_reserves_nothing_and_fills_the_monitor()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(2, desktop, () => { });

        edge.Place(Panel(Edges.Left | Edges.Top | Edges.Right | Edges.Bottom, covers: true), true, false);

        Assert.Null(desktop.Asked);
        Assert.Equal(0, desktop.Registers);
        Assert.Equal(Primary.Bounds, desktop.Moved);
    }

    [Fact]
    public void A_corner_panel_uses_the_work_area_and_a_hidden_one_reserves_nothing()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(3, desktop, () => { });

        edge.Place(Panel(Edges.Top | Edges.Left), true, false);
        Assert.Equal(0, desktop.Registers);
        Assert.Equal(Primary.Area.Top, desktop.Moved!.Value.Top);

        edge.Place(Panel(Edges.Top | Edges.Left | Edges.Right), false, false);
        Assert.Equal(0, desktop.Registers);
        Assert.Equal(Primary.Area.Top, desktop.Moved.Value.Top);
    }

    [Fact]
    public void A_panel_steps_behind_a_fullscreen_app_and_comes_back_when_it_leaves()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(4, desktop, () => { });
        var panel = Panel(Edges.Top | Edges.Left);

        edge.Place(panel, true, false);
        Assert.True(desktop.Topmost);

        desktop.Fullscreen = true;
        edge.Place(panel, true, false);
        Assert.False(desktop.Topmost);

        desktop.Fullscreen = false;
        edge.Place(panel, true, false);
        Assert.True(desktop.Topmost);

        edge.Place(Panel(Edges.Top | Edges.Left, above: false), true, false);
        Assert.False(desktop.Topmost);
    }

    [Fact]
    public void A_placement_that_arrives_after_the_panel_is_gone_reserves_no_edge()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(5, desktop, () => { });
        var panel = Panel(Edges.Top | Edges.Left | Edges.Right);

        edge.Place(panel, true, false);
        edge.Remove();
        Assert.Equal(1, desktop.Removes);
        Assert.Equal(1, desktop.Reserves);

        Assert.False(edge.Place(panel, true, true));

        Assert.Equal(1, desktop.Registers);
        Assert.Equal(1, desktop.Reserves);
    }

    [Fact]
    public void Explorer_restarting_drops_the_edge_and_places_the_panel_again()
    {
        var desktop = new Desktop();
        var panel = Panel(Edges.Bottom | Edges.Left | Edges.Right);
        PanelEdge edge = null!;
        edge = new PanelEdge(6, desktop, () => edge.Place(panel, true, false));
        edge.Place(panel, true, false);
        Assert.Equal(1, desktop.Registers);

        edge.ExplorerRestarted();

        Assert.Equal(1, desktop.Removes);
        Assert.Equal(1, desktop.Reserves);
        desktop.Posted.Single()();
        Assert.Equal(2, desktop.Registers);
        Assert.Equal(2, desktop.Reserves);
        edge.Remove();
    }

    [Fact]
    public void A_crash_gives_back_every_edge_the_shell_holds()
    {
        var desktop = new Desktop();
        var edge = new PanelEdge(7, desktop, () => { });
        edge.Place(Panel(Edges.Top | Edges.Left | Edges.Right), true, false);
        Assert.Equal(1, desktop.Registers);

        PanelEdge.ReleaseAll();

        Assert.Equal(1, desktop.Removes);
    }
}
