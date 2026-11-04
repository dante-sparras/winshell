using Winshell.Notifications;

namespace Winshell.Services.Tests;

public sealed class ToastChangesTests
{
    private readonly FakeTimeProvider _time = new();
    private string? _stamp = "1024 1";

    private ToastChanges Changes() => new(() => _stamp, _time);

    [Fact]
    public void The_toasts_are_read_again_when_the_database_changes_and_at_least_every_minute()
    {
        var changes = Changes();
        Assert.True(changes.Due(out var current));
        changes.Read(current);

        _time.Advance(TimeSpan.FromSeconds(50));
        Assert.False(changes.Due(out _));
        _stamp = "2048 2";
        Assert.True(changes.Due(out current));
        changes.Read(current);

        _time.Advance(ToastChanges.MaxAge - TimeSpan.FromSeconds(1));
        Assert.False(changes.Due(out _));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(changes.Due(out current));
        changes.Read(current);

        // A database that cannot be read says nothing, so every poll reads.
        _stamp = null;
        Assert.True(changes.Due(out current));
        changes.Read(current);
        Assert.True(changes.Due(out _));
    }

    [Fact]
    public void A_change_during_a_read_and_a_read_that_failed_leave_the_toasts_due()
    {
        var changes = Changes();
        Assert.True(changes.Due(out var current));
        changes.Change();
        changes.Read(current);
        Assert.True(changes.Due(out current));
        changes.Read(current);
        Assert.False(changes.Due(out _));

        changes.Change();
        Assert.True(changes.Due(out _));
        // That read failed, so nothing was read.
        Assert.True(changes.Due(out current));
        changes.Read(current);
        Assert.False(changes.Due(out _));
    }
}
