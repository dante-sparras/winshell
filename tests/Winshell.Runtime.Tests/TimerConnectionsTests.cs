namespace Winshell.Runtime.Tests;

public class TimerConnectionsTests
{
    [Fact]
    public void A_one_shot_timer_triggers_before_it_stops()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              property var order: []
              property Timer clock: Timer {
                interval: 20
                running: true
                onTriggered: order.push("triggered:" + running)
                onRunningChanged: if (!running) order.push("stopped")
              }
            }
            """, out var shell);
        using var _ = shell;
        shell.Loop.Advance(TimeSpan.FromMilliseconds(20));
        var order = ((List<object?>)shell.Eval(root, "order")!).Cast<string>();
        Assert.Equal(["triggered:true", "stopped"], order);
        var clock = (QObject)root.ReadProperty("clock")!;
        Assert.False((bool)clock.ReadProperty("running")!);
    }

    [Fact]
    public void Connections_target_null_drops_the_owner()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              id: root
              signal ping()
              property int hits: 0
              property QtObject other: QtObject { signal ping() }
              property Connections wires: Connections {
                function onPing() { root.hits = root.hits + 1 }
              }
            }
            """, out var shell);
        using var _ = shell;
        shell.Eval(root, "ping()");
        Assert.Equal(1, root.ReadProperty("hits"));

        var wires = (QObject)root.ReadProperty("wires")!;
        wires.WriteProperty("target", null);
        shell.Eval(root, "ping()");
        Assert.Equal(1, root.ReadProperty("hits"));

        var other = (QObject)root.ReadProperty("other")!;
        wires.WriteProperty("target", other);
        shell.Eval(root, "ping()");
        Assert.Equal(1, root.ReadProperty("hits"));
        shell.Eval(other, "ping()");
        Assert.Equal(2, root.ReadProperty("hits"));

        wires.WriteProperty("target", root);
        shell.Eval(root, "ping()");
        Assert.Equal(3, root.ReadProperty("hits"));
    }
}
