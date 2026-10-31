namespace Winshell.Runtime.Tests;

/// <summary>A PropertyAction in a transition writes the state's value when the sequence reaches it.</summary>
// Theme.Current is one record for the process. AnimationEffectsTests turns animations off while it runs, and a
// transition that finishes at once would commit visible: false before this test can read the held value.
[Collection(ThemeCollection.Name)]
public class PropertyActionTests
{
    [Fact]
    public void A_property_action_holds_its_value_until_the_sequence_reaches_it()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: box
              opacity: 1
              visible: true
              height: 10
              property bool ran: false
              states: [
                State {
                  name: "gone"
                  PropertyChanges { target: box; opacity: 0; visible: false; height: 40 }
                }
              ]
              transitions: [
                Transition {
                  SequentialAnimation {
                    NumberAnimation { property: "opacity"; duration: 1000 }
                    PropertyAction { property: "visible" }
                    ScriptAction { script: function() { box.ran = true } }
                  }
                }
              ]
            }
            """));
        var box = shell.Load();
        shell.Eval(box, "state = \"gone\"");

        Assert.True(Convert.ToBoolean(box.ReadProperty("visible")));
        Assert.Equal(40d, Convert.ToDouble(box.ReadProperty("height")));
        Assert.Equal(1d, Convert.ToDouble(box.ReadProperty("opacity")));
        Assert.False(Convert.ToBoolean(box.ReadProperty("ran")));

        shell.Loop.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(Convert.ToBoolean(box.ReadProperty("visible")));
        Assert.False(Convert.ToBoolean(box.ReadProperty("ran")));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("opacity")), 0.4, 0.8);

        shell.Loop.Advance(TimeSpan.FromMilliseconds(700));
        Assert.False(Convert.ToBoolean(box.ReadProperty("visible")));
        Assert.True(Convert.ToBoolean(box.ReadProperty("ran")));
        Assert.InRange(Convert.ToDouble(box.ReadProperty("opacity")), 0, 0.05);
    }

    [Fact]
    public void A_script_action_in_a_transition_runs()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              id: box
              property int n: 0
              property bool ran: false
              states: [
                State {
                  name: "go"
                  PropertyChanges { target: box; n: 1 }
                }
              ]
              transitions: [
                Transition {
                  ScriptAction { script: function() { box.ran = true } }
                }
              ]
            }
            """));
        var box = shell.Load();
        shell.Eval(box, "state = \"go\"");
        Assert.Equal(1, Convert.ToInt32(box.ReadProperty("n")));
        Assert.True(Convert.ToBoolean(box.ReadProperty("ran")));
    }
}
