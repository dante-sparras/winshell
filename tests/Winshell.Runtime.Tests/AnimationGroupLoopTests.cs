using System.Globalization;

namespace Winshell.Runtime.Tests;

/// <summary>A member's loops play one after another inside a group, and a rotation keeps its direction.</summary>
public class AnimationGroupLoopTests
{
    [Fact]
    public void A_sequential_member_repeats_instead_of_stretching()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: box
              property real place: 0
              property bool done: false
              SequentialAnimation {
                running: true
                NumberAnimation {
                  target: box
                  property: "place"
                  from: 0
                  to: 100
                  duration: 1000
                  loops: 3
                }
                ScriptAction { script: function() { box.done = true } }
              }
            }
            """));
        var box = shell.Load();

        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("place"), CultureInfo.InvariantCulture), 40, 60);
        Assert.False(Convert.ToBoolean(box.ReadProperty("done"), CultureInfo.InvariantCulture));

        shell.Loop.Advance(TimeSpan.FromMilliseconds(2000));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("place"), CultureInfo.InvariantCulture), 40, 60);
        Assert.False(Convert.ToBoolean(box.ReadProperty("done"), CultureInfo.InvariantCulture));

        shell.Loop.Advance(TimeSpan.FromMilliseconds(600));
        Assert.True(Convert.ToBoolean(box.ReadProperty("done"), CultureInfo.InvariantCulture));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("place"), CultureInfo.InvariantCulture), 90, 100);
    }

    [Fact]
    public void A_parallel_member_repeats_instead_of_holding()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: box
              property real place: 0
              ParallelAnimation {
                running: true
                NumberAnimation {
                  target: box
                  property: "place"
                  from: 0
                  to: 100
                  duration: 1000
                  loops: 3
                }
              }
            }
            """));
        var box = shell.Load();

        shell.Loop.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("place"), CultureInfo.InvariantCulture), 40, 60);

        shell.Loop.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("place"), CultureInfo.InvariantCulture), 90, 100);
    }

    [Theory]
    [InlineData(1, 350, 10)]
    [InlineData(2, 10, 350)]
    public void A_rotation_follows_its_direction(int direction, double from, double to)
    {
        using var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            Item {
              id: box
              rotation: 0
              RotationAnimation {
                running: true
                target: box
                property: "rotation"
                from: {{from}}
                to: {{to}}
                duration: 1000
                direction: {{direction}}
              }
            }
            """));
        var box = shell.Load();
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("rotation"), CultureInfo.InvariantCulture), 340, 380);
    }
}
