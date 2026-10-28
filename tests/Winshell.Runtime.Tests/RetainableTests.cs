using Winshell.Runtime.Quickshell;

namespace Winshell.Runtime.Tests;

public class RetainableTests
{
    [Fact]
    public void A_lock_holds_the_object_until_it_is_released()
    {
        var hold = new Hold();
        var dropped = false;
        var destroying = false;
        var hook = RetainableHook.Get(hold, create: true)!;
        hook.Connect("dropped", _ => dropped = true);
        hook.Connect("aboutToDestroy", _ => destroying = true);
        hook.Lock();

        RetainableHook.Release(hold);

        Assert.True(dropped);
        Assert.True(hook.Retained);
        Assert.False(destroying);
        Assert.False(hold.IsDestroyed);

        hook.Unlock();

        Assert.True(destroying);
        Assert.True(hold.IsDestroyed);
    }

    [Fact]
    public void An_object_with_no_lock_is_destroyed_when_it_is_released()
    {
        var hold = new Hold();
        RetainableHook.Release(hold);
        Assert.True(hold.IsDestroyed);
    }

    [Fact]
    public void Force_unlock_destroys_a_dropped_object()
    {
        var hold = new Hold();
        var hook = RetainableHook.Get(hold, create: true)!;
        hook.Lock();
        RetainableHook.Release(hold);
        hook.ForceUnlock();
        Assert.True(hold.IsDestroyed);
    }

    [Fact]
    public void Unlocking_more_times_than_locking_is_reported()
    {
        using var shell = new TestShell();
        var hold = new Hold { Engine = shell.Engine };
        var hook = RetainableHook.Get(hold, create: true)!;
        hook.Unlock();
        Assert.Contains(shell.Messages, message => message.Contains("unlocked more times", StringComparison.Ordinal));
        Assert.False(hold.IsDestroyed);
    }

    [Fact]
    public void A_retainable_lock_keeps_its_object_while_locked()
    {
        var hold = new Hold();
        var locker = new RetainableLock { Object = hold, Locked = true };
        var dropped = false;
        locker.Connect("dropped", _ => dropped = true);

        RetainableHook.Release(hold);

        Assert.True(dropped);
        Assert.True(locker.Retained);
        Assert.False(hold.IsDestroyed);

        locker.Locked = false;

        Assert.True(hold.IsDestroyed);
        Assert.False(locker.Retained);
    }

    [Fact]
    public void A_lock_refuses_an_object_that_cannot_be_retained()
    {
        using var shell = new TestShell();
        var plain = new QObject { Engine = shell.Engine };
        var locker = new RetainableLock { Engine = shell.Engine, Object = plain };
        Assert.Null(locker.Object);
        Assert.Contains(shell.Messages, message => message.Contains("non-retainable", StringComparison.Ordinal));
    }

    [Fact]
    public void RetainableLock_loads_in_qml()
    {
        var root = TestShell.Run("""
            import QtQuick
            import Quickshell
            Item {
              property alias hold: hold
              RetainableLock { id: hold }
            }
            """, out var shell);
        using var _ = shell;
        var hold = (RetainableLock)root.ReadProperty("hold")!;
        Assert.False(hold.Locked);
        Assert.False(hold.Retained);
        Assert.Null(hold.Object);
    }

    private sealed class Hold : QObject, IRetainable
    {
        public RetainableHook? RetainHook { get; set; }

        public void FinishRetain() => Destroy();
    }
}
