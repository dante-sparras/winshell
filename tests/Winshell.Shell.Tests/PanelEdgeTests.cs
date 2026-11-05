using Microsoft.UI.Xaml.Controls;
using Winshell.Diagnostics;
using Winshell.Native;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
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
                ReservedSize = 8,
            };
            panel.Visible = true;
            var host = panel.Host!;
            panel.Dispose();

            // Explorer tells a bar when another bar moves, and that notice can land on the loop after the bar is gone.
            host.Placement.PlacePanel();

            return host.Placement.AppBar?.Registered == true;
        });

        Assert.False(holds);
    }

    [Fact]
    public async Task The_doctor_knows_a_panel_for_Winshell_s_own()
    {
        var classes = await ShellFixture.On(_ =>
        {
            using var panel = new PanelWindow { Anchors = Edges.Top | Edges.Left, Width = 320, Height = 60, Content = new Grid() };
            panel.Visible = true;
            return DoctorProbe.EdgeWindows().Select(window => window.ClassName).ToList();
        });

        Assert.Contains(DoctorScreens.WinshellClass, classes);
    }

    [Fact]
    public async Task A_panel_goes_behind_a_fullscreen_app_and_comes_back_when_it_leaves()
    {
        await ShellFixture.On(async _ =>
        {
            using var app = new FloatingWindow { Width = 300, Height = 200, Content = new Grid() };
            app.Visible = true;
            // A corner panel reserves nothing, so Explorer sends it no notice about a fullscreen app.
            using var panel = new PanelWindow { Anchors = Edges.Top | Edges.Left, Width = 320, Height = 60, Content = new Grid() };
            panel.Visible = true;
            var host = panel.Host!;
            var appHost = app.Host!;

            try
            {
                app.Fullscreen = true;
                Win32.SetForegroundWindow(appHost.Handle);
                await Wait.UntilAsync(() => InFront(appHost.Handle, host.Handle), "the panel to go behind the fullscreen app");
                app.Fullscreen = false;
                await Wait.UntilAsync(() => InFront(host.Handle, appHost.Handle), "the panel to come back when the app leaves");
            }
            finally
            {
                app.Fullscreen = false;
            }
            return 0;
        });
    }

    private static bool InFront(nint window, nint other)
    {
        const uint next = 2;
        for (var at = window; at != 0; at = Win32.GetWindow(at, next))
        {
            if (at == other)
                return true;
        }
        return false;
    }
}
