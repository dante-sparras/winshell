using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>A tray icon shows the app's tooltip when the pointer rests on it.</summary>
[Collection(ThemeCollection.Name)]
public class TrayTooltipTests
{
    private const string Icon = """
        import QtQuick
        Item {
          TrayIcon { objectName: "withTip"; item: ({ tooltip: "Sync\nUp to date", appName: "Sync" }) }
          TrayIcon { objectName: "noTip"; item: ({ tooltip: "", appName: "Updater" }) }
        }
        """;

    private static ToolTipAttached Tip(DefaultShellHarness shell, string name) =>
        Assert.IsType<ToolTipAttached>(shell.Find(name).FindAttached(typeof(ToolTipAttached)));

    [Fact]
    public void A_tray_icon_has_its_tooltip_and_falls_back_to_the_app_name()
    {
        using var shell = new DefaultShellHarness(Icon);

        Assert.Equal("Sync\nUp to date", Tip(shell, "withTip").Text);
        Assert.Equal("Updater", Tip(shell, "noTip").Text);
    }

    [Fact]
    public void The_tooltip_stays_away_until_the_pointer_is_on_the_icon()
    {
        using var shell = new DefaultShellHarness(Icon);

        Assert.False(Tip(shell, "noTip").Visible);

        shell.Scene.PointerMove(10, 10);
        shell.Settle();

        Assert.True(Tip(shell, "noTip").Visible);
    }
}
