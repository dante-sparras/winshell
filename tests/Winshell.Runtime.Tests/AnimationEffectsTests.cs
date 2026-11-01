using System.Globalization;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

[Collection(ThemeCollection.Name)]
public class AnimationEffectsTests
{
    [Fact]
    public void An_animation_jumps_to_its_end_when_effects_are_off()
    {
        var saved = Theme.Current;
        Theme.Current = saved with { AnimationsEnabled = false };
        try
        {
            using var shell = new TestShell(("shell.qml", """
                import QtQuick
                Item {
                  id: root
                  property real place: 0
                  property real lap: 0
                  property bool stillRunning: once.running
                  property bool loopRunning: forever.running
                  NumberAnimation {
                    id: once
                    running: true
                    target: root
                    property: "place"
                    from: 0
                    to: 100
                    duration: 1000
                  }
                  NumberAnimation {
                    id: forever
                    running: true
                    loops: -1
                    target: root
                    property: "lap"
                    from: 0
                    to: 40
                    duration: 1000
                  }
                }
                """));
            var root = shell.Load();
            shell.Loop.Process();
            Assert.Equal(100, Convert.ToDouble(root.ReadProperty("place"), CultureInfo.InvariantCulture));
            Assert.False((bool)root.ReadProperty("stillRunning")!);
            Assert.Equal(40, Convert.ToDouble(root.ReadProperty("lap"), CultureInfo.InvariantCulture));
            Assert.False((bool)root.ReadProperty("loopRunning")!);
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void A_flick_scroll_moves_at_once_when_effects_are_off()
    {
        var saved = Theme.Current;
        Theme.Current = saved with { AnimationsEnabled = false };
        try
        {
            using var shell = new TestShell(("shell.qml", """
                import QtQuick
                Flickable { objectName: "flick"; width: 100; height: 100; contentWidth: 100; contentHeight: 400 }
                """));
            var flick = (Flickable)shell.Load();
            flick.ScrollBy(0, 100, true);
            Assert.Equal(100, flick.ContentY);
        }
        finally
        {
            Theme.Current = saved;
        }
    }
}
