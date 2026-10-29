namespace Winshell.Runtime.Tests;

/// <summary>One animation that throws must not clear the process-wide clock.</summary>
public class AnimationClockTests
{
    [Fact]
    public void A_throwing_script_action_leaves_other_animations_running()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              property real x: 0
              SequentialAnimation {
                running: true
                PauseAnimation { duration: 30 }
                ScriptAction { script: function() { throw new Error("boom") } }
              }
              NumberAnimation {
                running: true
                target: root
                property: "x"
                from: 0
                to: 100
                duration: 1000
              }
            }
            """));
        var root = shell.Load();

        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        var midway = Convert.ToDouble(root.ReadProperty("x"));
        Assert.True(midway > 1, $"x was {midway}");
        Assert.Contains(shell.Messages, m => m.Contains("boom", StringComparison.Ordinal));

        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        Assert.True(Convert.ToDouble(root.ReadProperty("x")) > midway);
    }
}
