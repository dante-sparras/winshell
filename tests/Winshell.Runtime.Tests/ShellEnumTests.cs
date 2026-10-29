using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>The shell's enum types take their numbers from the C# enum, which is the only copy.</summary>
public class ShellEnumTests
{
    [Fact]
    public void The_registered_values_match_the_c_sharp_enums()
    {
        var modules = StandardModules.Create();
        var core = modules.Module("Winshell");
        Assert.Equal((int)PanelWindow.ExclusionModeValue.Ignore, core.Types["ExclusionMode"].Enums["Ignore"]);
        Assert.Equal((int)PanelWindow.ExclusionModeValue.Auto, core.Types["ExclusionMode"].Enums["Auto"]);
        Assert.Equal((int)Region.ShapeValue.Ellipse, core.Types["RegionShape"].Enums["Ellipse"]);
        Assert.Equal((int)Intersection.Xor, core.Types["Intersection"].Enums["Xor"]);
        Assert.Equal((int)ObjectComparison.Identity, core.Types["ObjectComparison"].Enums["Identity"]);
        Assert.Equal((int)PopupAdjustment.All, core.Types["PopupAdjustment"].Enums["All"]);
        Assert.Equal(63, (int)PopupAdjustment.All);
        Assert.Equal(Enum.GetValues<PopupAdjustment>().Length, core.Types["PopupAdjustment"].Enums.Count);
        Assert.Equal((int)MenuButtonType.RadioButton, core.Types["MenuButtonType"].Enums["RadioButton"]);
        Assert.Equal((int)FileView.FileViewError.PermissionDenied, modules.Module("Winshell.Io").Types["FileViewError"].Enums["PermissionDenied"]);

        var root = TestShell.Run("""
            import QtQuick
            import Winshell
            Item {
              property int mode: ExclusionMode.Ignore
              property string name: ExclusionMode.toString(ExclusionMode.Auto)
              property int fit: PopupAdjustment.All
            }
            """, out var shell);
        using var _ = shell;
        Assert.Equal((int)PanelWindow.ExclusionModeValue.Ignore, root.ReadProperty("mode"));
        Assert.Equal(nameof(PanelWindow.ExclusionModeValue.Auto), root.ReadProperty("name"));
        Assert.Equal((int)PopupAdjustment.All, root.ReadProperty("fit"));
    }
}
