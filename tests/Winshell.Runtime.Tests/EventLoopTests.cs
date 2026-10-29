using System.Runtime.CompilerServices;

namespace Winshell.Runtime.Tests;

/// <summary>One <see cref="EventQueue.RunDue"/> pass must return even when the work it runs queues more.</summary>
public class EventLoopTests
{
    [Fact]
    public void A_posted_action_that_posts_itself_runs_once_per_pass()
    {
        var loop = new ManualEventLoop();
        var ran = 0;
        loop.Post(() =>
        {
            ran++;
            loop.Post(() => ran++);
        });

        Assert.True(loop.RunDue());
        Assert.Equal(1, ran);
        Assert.True(loop.RunDue());
        Assert.Equal(2, ran);
        Assert.False(loop.RunDue());
    }

    [Fact]
    public void A_timer_that_reschedules_itself_as_already_due_runs_once_per_pass()
    {
        var loop = new ManualEventLoop();
        var fires = 0;
        void Tick()
        {
            fires++;
            loop.Schedule(TimeSpan.Zero, Tick);
        }

        loop.Schedule(TimeSpan.Zero, Tick);
        Assert.True(loop.RunDue());
        Assert.Equal(1, fires);
        Assert.True(loop.RunDue());
        Assert.Equal(2, fires);
    }

    [Fact]
    public void A_one_millisecond_repeating_timer_fires_once_per_interval()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int hits: 0
              property Timer clock: Timer {
                interval: 1
                repeat: true
                running: true
                onTriggered: hits++
              }
            }
            """));
        var root = shell.Engine.CreateRoot(Path.Combine(shell.Directory, "shell.qml"));

        Assert.Equal(0, root.ReadProperty("hits"));
        shell.Loop.Advance(TimeSpan.FromMilliseconds(5));
        Assert.Equal(5, root.ReadProperty("hits"));
    }

    [Fact]
    public void Qt_callLater_that_queues_itself_returns_to_the_loop()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQml
            QtObject {
              property int hits: 0
              function again() {
                hits++
                if (hits < 5)
                  Qt.callLater(again)
              }
              Component.onCompleted: Qt.callLater(again)
            }
            """));
        var root = shell.Engine.CreateRoot(Path.Combine(shell.Directory, "shell.qml"));

        Assert.Equal(0, root.ReadProperty("hits"));
        Assert.True(shell.Loop.RunDue());
        Assert.Equal(1, root.ReadProperty("hits"));
        shell.Loop.Process();
        Assert.Equal(5, root.ReadProperty("hits"));
    }

    [Fact]
    public void A_cancelled_timer_does_not_hold_its_action_until_its_due_time()
    {
        var loop = new ManualEventLoop();
        var action = ScheduleAndCancel(loop);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(action.IsAlive);
        GC.KeepAlive(loop);
    }

    [Fact]
    public void Timers_keep_their_order_when_cancelled_ones_are_dropped()
    {
        var loop = new ManualEventLoop();
        var fired = new List<int>();
        var first = loop.Schedule(TimeSpan.FromMilliseconds(10), () => fired.Add(1));
        loop.Schedule(TimeSpan.FromMilliseconds(20), () => fired.Add(2));
        loop.Schedule(TimeSpan.FromMilliseconds(20), () => fired.Add(3));
        for (var i = 0; i < 10; i++)
            loop.Schedule(TimeSpan.FromMilliseconds(5), () => fired.Add(0)).Dispose();
        first.Dispose();

        loop.Advance(TimeSpan.FromMilliseconds(30));

        Assert.Equal([2, 3], fired);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ScheduleAndCancel(EventQueue loop)
    {
        var payload = new object();
        Action action = () => GC.KeepAlive(payload);
        loop.Schedule(TimeSpan.FromHours(1), action).Dispose();
        return new WeakReference(action);
    }
}
