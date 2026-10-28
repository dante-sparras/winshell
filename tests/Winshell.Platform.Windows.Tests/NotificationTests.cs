using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Tests;

public class NotificationTests
{
    [Fact]
    public void A_notification_is_kept_only_when_the_handler_tracks_it()
    {
        var server = new NotificationServer(listen: false);
        ShellNotification? seen = null;
        server.Connect("notification", args =>
        {
            seen = (ShellNotification)args[0]!;
            seen.Tracked = true;
        });

        server.Deliver(Sample(), 7, lastGeneration: false);

        Assert.NotNull(seen);
        Assert.Equal(7, seen.Id);
        Assert.Equal("Mail", seen.AppName);
        Assert.True(seen.Tracked);
        Assert.Same(seen, Assert.Single(server.TrackedNotifications.Rows));
    }

    [Fact]
    public void An_untracked_notification_is_discarded()
    {
        var server = new NotificationServer(listen: false);
        ShellNotification? seen = null;
        server.Connect("notification", args => seen = (ShellNotification)args[0]!);
        server.Deliver(Sample(), 1, lastGeneration: true);
        Assert.NotNull(seen);
        Assert.True(seen.LastGeneration);
        Assert.True(seen.IsDestroyed);
        Assert.Empty(server.TrackedNotifications.Rows);
    }

    [Fact]
    public void Dismiss_closes_the_notification_and_drops_it()
    {
        var server = new NotificationServer(listen: false);
        ShellNotification? seen = null;
        var reason = 0;
        server.Connect("notification", args =>
        {
            seen = (ShellNotification)args[0]!;
            seen.Tracked = true;
            seen.Connect("closed", closed => reason = (int)closed[0]!);
        });
        server.Deliver(Sample(), 3, lastGeneration: false);

        seen!.Dismiss();

        Assert.Equal((int)NotificationCloseReason.Dismissed, reason);
        Assert.True(seen.IsDestroyed);
        Assert.Empty(server.TrackedNotifications.Rows);
    }

    [Fact]
    public void A_cached_toast_is_replayed_only_when_keep_on_reload_is_set()
    {
        var server = new NotificationServer(listen: false);
        server.KeepOnReload = false;
        ShellNotification? seen = null;
        server.Connect("notification", args =>
        {
            seen = (ShellNotification)args[0]!;
            seen.Tracked = true;
        });

        server.Offer(Sample(), 4, prior: true);

        Assert.Null(seen);
        Assert.Empty(server.TrackedNotifications.Rows);

        server.KeepOnReload = true;
        server.Offer(Sample(), 4, prior: true);

        Assert.NotNull(seen);
        Assert.True(seen!.LastGeneration);
        Assert.Same(seen, Assert.Single(server.TrackedNotifications.Rows));
    }

    [Fact]
    public void A_new_toast_arrives_even_when_keep_on_reload_is_off()
    {
        var server = new NotificationServer(listen: false);
        server.KeepOnReload = false;
        ShellNotification? seen = null;
        server.Connect("notification", args =>
        {
            seen = (ShellNotification)args[0]!;
            seen.Tracked = true;
        });

        server.Offer(Sample(), 8, prior: false);

        Assert.NotNull(seen);
        Assert.False(seen!.LastGeneration);
    }

    [Fact]
    public void A_locked_notification_stays_until_the_lock_is_released()
    {
        var server = new NotificationServer(listen: false);
        ShellNotification? seen = null;
        server.Connect("notification", args =>
        {
            seen = (ShellNotification)args[0]!;
            seen.Tracked = true;
        });
        server.Deliver(Sample(), 9, lastGeneration: false);
        var hook = RetainableHook.Get(seen!, create: true)!;
        hook.Lock();

        seen!.Dismiss();

        Assert.True(hook.Retained);
        Assert.False(seen.IsDestroyed);
        Assert.Empty(server.TrackedNotifications.Rows);

        hook.Unlock();

        Assert.True(seen.IsDestroyed);
    }

    [Fact]
    public void Keep_on_reload_is_fixed_once_the_server_is_accepting()
    {
        var server = new NotificationServer(listen: false);
        server.MarkAccepting();
        server.KeepOnReload = false;
        Assert.True(server.KeepOnReload);
    }

    private static IncomingToast Sample() => new(42, "Mail", "Hello", "A message", "", "Mail.App", 5);
}
