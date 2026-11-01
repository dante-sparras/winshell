using System.Globalization;

namespace Winshell.Runtime.Tests;

public class AnimationPauseTests
{
    [Fact]
    public void Pausing_an_animation_does_not_count_as_elapsed_time()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: root
              property real place: 0
              property bool hold: false
              NumberAnimation {
                running: true
                paused: hold
                target: root
                property: "place"
                from: 0
                to: 100
                duration: 1000
              }
            }
            """));
        var root = shell.Load();
        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        var before = Convert.ToDouble(root.ReadProperty("place"), CultureInfo.InvariantCulture);
        Assert.InRange(before, 15, 25);

        shell.Eval(root, "hold = true");
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.InRange(Convert.ToDouble(root.ReadProperty("place"), CultureInfo.InvariantCulture), before - 1, before + 1);

        shell.Eval(root, "hold = false");
        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        Assert.InRange(Convert.ToDouble(root.ReadProperty("place"), CultureInfo.InvariantCulture), before + 15, before + 25);
    }
}
