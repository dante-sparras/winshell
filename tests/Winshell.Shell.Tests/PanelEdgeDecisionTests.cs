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

    // A 150% 2560×1440 monitor to the right of a 1920×1080 primary, and its work area above a 48 px taskbar.
    private static readonly RECT Wide = new(1920, 0, 4480, 1440);
    private static readonly RECT WideWork = new(1920, 0, 4480, 1392);

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

        public List<PanelMonitor> Monitors { get; } = [Second, Scaled];

        public PanelMonitor? Find(string id)
        {
            foreach (var monitor in Monitors)
                if (monitor.Id == id)
                    return monitor;
            return null;
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

    private static PanelPlacement Anchored(Edges anchors, double width = 0, double height = 0, double left = 0,
        double top = 0, double right = 0, double bottom = 0)
    {
        return new PanelPlacement(null, anchors, left, top, right, bottom, width, height, false, 0, true, false);
    }

    /// <summary>Where a panel that reserves nothing goes on a monitor with this work area, in physical pixels.</summary>
    private static RECT PlacedIn(RECT bounds, RECT area, double scale, PanelPlacement panel)
    {
        var desktop = new Desktop();
        desktop.Monitors.Add(new PanelMonitor("anchored", bounds, area, scale));
        new PanelEdge(9, desktop, () => { }).Place(panel with { Screen = Screen("anchored") }, true, false);
        return desktop.Moved!.Value;
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

    [Fact]
    public void A_top_bar_spans_its_area_at_the_monitor_scale()
    {
        var rect = PlacedIn(Wide, WideWork, 1.5, Anchored(Edges.Top | Edges.Left | Edges.Right, height: 32));
        Assert.Equal(new RECT(1920, 0, 4480, 48), rect);
    }

    [Fact]
    public void A_bottom_bar_follows_the_work_area_when_the_taskbar_moves()
    {
        var bar = Anchored(Edges.Bottom | Edges.Left | Edges.Right, height: 40);
        Assert.Equal(new RECT(1920, 1332, 4480, 1392), PlacedIn(Wide, WideWork, 1.5, bar));

        // The taskbar auto-hides: the work area is the whole monitor, and the bar moves down to its edge.
        Assert.Equal(new RECT(1920, 1380, 4480, 1440), PlacedIn(Wide, Wide, 1.5, bar));
    }

    [Fact]
    public void A_corner_panel_keeps_its_margins_in_logical_pixels()
    {
        var rect = PlacedIn(Wide, WideWork, 1.5, Anchored(Edges.Bottom | Edges.Right, 200, 100, right: 8, bottom: 8));
        Assert.Equal(new RECT(4480 - 12 - 300, 1392 - 12 - 150, 4480 - 12, 1392 - 12), rect);
    }

    [Fact]
    public void An_unanchored_panel_is_centered()
    {
        var rect = PlacedIn(Primary.Bounds, Primary.Bounds, 1, Anchored(Edges.None, 400, 300));
        Assert.Equal(new RECT(760, 390, 1160, 690), rect);
    }

    [Fact]
    public void The_same_panel_is_placed_again_at_a_new_scale()
    {
        // The monitor's scale goes from 100% to 150%: the bar keeps 32 logical pixels.
        var bar = Anchored(Edges.Top | Edges.Left | Edges.Right, height: 32);
        Assert.Equal(32, PlacedIn(Wide, Wide, 1, bar).Height);
        Assert.Equal(48, PlacedIn(Wide, Wide, 1.5, bar).Height);
    }

    [Fact]
    public void A_panel_is_never_empty()
    {
        var rect = PlacedIn(Primary.Bounds, Primary.Bounds, 1,
            Anchored(Edges.Left | Edges.Right, height: 0, left: 1000, right: 1000));
        Assert.True(rect.Width >= 1);
        Assert.True(rect.Height >= 1);
    }
}
