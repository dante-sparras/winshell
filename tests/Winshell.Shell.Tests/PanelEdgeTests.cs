using Microsoft.UI.Xaml.Controls;
using Winshell.Diagnostics;

namespace Winshell.ShellTests;

/// <summary>A real panel window. Fullscreen stepping and a placement after release are fast tests.</summary>
[Trait("Category", "Slow")]
public sealed class PanelEdgeTests
{
    [Fact]
    public async Task The_doctor_knows_a_panel_for_Winshell_s_own()
    {
        var classes = await ShellFixture.On(_ =>
        {
            using var panel = new PanelWindow
                { Anchors = Edges.Top | Edges.Left, Width = 320, Height = 60, Content = new Grid() };
            panel.Visible = true;
            return DoctorProbe.EdgeWindows().Select(window => window.ClassName).ToList();
        });

        Assert.Contains(DoctorScreens.WinshellClass, classes);
    }
}
