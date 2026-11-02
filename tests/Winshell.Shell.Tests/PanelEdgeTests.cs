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
            host.PlacePanel();

            return host.HoldsEdge;
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

        Assert.Contains(Doctor.WinshellClass, classes);
    }

    [Fact]
    public async Task A_panel_goes_behind_a_fullscreen_app_and_comes_back_when_it_leaves()
    {
        var (covered, back) = await ShellFixture.On(_ =>
        {
            using var app = new FloatingWindow { Width = 300, Height = 200, Content = new Grid() };
            app.Visible = true;
            using var panel = new PanelWindow { Anchors = Edges.Top | Edges.Left, Width = 320, Height = 60, Content = new Grid() };
            panel.Visible = true;
            var host = panel.Host!;

            // Explorer tells an app bar when a fullscreen window takes its screen, and when it leaves.
            Win32.SendMessageW(host.Handle, HostWindow.AppBarMessage, (nint)Win32.ABN_FULLSCREENAPP, 1);
            host.PlacePanel();
            var behind = InFront(app.Host!.Handle, host.Handle);
            Win32.SendMessageW(host.Handle, HostWindow.AppBarMessage, (nint)Win32.ABN_FULLSCREENAPP, 0);
            host.PlacePanel();

            return (behind, InFront(host.Handle, app.Host.Handle));
        });

        Assert.True(covered);
        Assert.True(back);
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
