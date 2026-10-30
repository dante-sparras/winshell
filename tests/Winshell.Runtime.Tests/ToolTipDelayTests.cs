using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>A tool tip waits Windows' hover time, 500 ms, before it shows.</summary>
public class ToolTipDelayTests
{
    [Fact]
    public void A_hover_tool_tip_shows_after_500_ms()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Controls
            Item {
              property bool tipped: false
              property int delay: ToolTip.delay
              property var tip: ToolTip.toolTip
              ToolTip.text: "Settings"
              ToolTip.visible: tipped
            }
            """));
        using var _ = shell;
        var root = (Item)shell.Load();
        Assert.Equal(500, root.ReadProperty("delay"));

        root.WriteProperty("tipped", true);
        shell.Loop.Process();
        var tip = (ToolTip)root.ReadProperty("tip")!;
        Assert.Equal(500, tip.Delay);
        Assert.False(tip.Opened);

        shell.Loop.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(tip.Opened);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(tip.Opened);
    }

    [Fact]
    public void A_tool_tip_waits_500_ms_by_default()
    {
        Assert.Equal(500, new ToolTip().Delay);
    }
}
