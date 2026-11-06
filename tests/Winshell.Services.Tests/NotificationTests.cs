using System.Buffers.Binary;
using System.ComponentModel;
using Winshell.Notifications;

namespace Winshell.Services.Tests;

/// <summary>Notifications over a fake backend: no toast is read from Windows, and none is dismissed.</summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static IncomingToast Toast(uint id, string appId = "Mail", string title = "Hello", int minutes = 0,
        IAppLogoSource? logo = null)
    {
        return new IncomingToast(id, appId, "Mail app", title, "Body", Noon.AddMinutes(minutes), Logo: logo);
    }

    private static NotificationSnapshot Snapshot(params IncomingToast[] toasts)
    {
        return new NotificationSnapshot(NotificationAccessStatus.Allowed, toasts, NotificationState.Off,
            NotificationMode.Unrestricted);
    }

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    private static NotificationListener Listener(FakeBackend backend, out ManualEventLoop loop, Log? log = null)
    {
        loop = new ManualEventLoop();
        return new NotificationListener(loop, log ?? Log.Default, backend);
    }

    [Fact]
    public void Notifications_are_newest_first_and_grouped_by_app()
    {
        using var listener = Listener(new FakeBackend(), out _);

        listener.Apply(Snapshot(Toast(1, minutes: 1), Toast(2, minutes: 3), Toast(3, "Chat", minutes: 2),
            Toast(4, "")));

        Assert.Equal([2u, 3u, 1u, 4u], listener.Notifications.Select(n => n.Id));
        Assert.Equal(["Mail", "Chat", ""], listener.Groups.Select(g => g.AppId));
        Assert.Equal([2u, 1u], listener.Groups[0].Notifications.Select(n => n.Id));
    }

    [Fact]
    public void A_notification_that_stays_is_the_same_object_and_only_what_differs_changes()
    {
        using var listener = Listener(new FakeBackend(), out _);
        listener.Apply(Snapshot(Toast(1), Toast(2, minutes: 1)));
        var kept = listener.Notifications.Single(n => n.Id == 1);
        var group = listener.Groups[0];
        var changes = Watch(kept);

        listener.Apply(Snapshot(Toast(1, title: "Updated")));

        Assert.Same(kept, Assert.Single(listener.Notifications));
        Assert.Same(group, Assert.Single(listener.Groups));
        Assert.Same(kept, Assert.Single(group.Notifications));
        Assert.Equal([nameof(Notification.Title)], changes);
        Assert.Equal("Updated", kept.Title);
    }

    [Fact]
    public void Added_fires_after_the_collections_hold_a_new_notification_and_once_for_each()
    {
        using var listener = Listener(new FakeBackend(), out _);
        var seen = new List<(uint Id, int Count, int Groups)>();
        listener.NotificationAdded += n => seen.Add((n.Id, listener.Notifications.Count, listener.Groups.Count));

        listener.Apply(Snapshot(Toast(1)));
        listener.Apply(Snapshot(Toast(1), Toast(2, "Chat")));

        Assert.Equal([(1u, 1, 1), (2u, 2, 2)], seen);
    }

    [Fact]
    public void Do_not_disturb_follows_the_notification_mode()
    {
        using var listener = Listener(new FakeBackend(), out _);
        Assert.Equal(NotificationState.Unknown, listener.DoNotDisturbState);
        listener.Apply(Snapshot());
        Assert.Equal(NotificationState.Off, listener.DoNotDisturbState);
        var changes = Watch(listener);

        listener.Apply(Snapshot() with { Mode = NotificationMode.AlarmsOnly, Focus = NotificationState.On });
        Assert.Equal(NotificationState.On, listener.DoNotDisturbState);
        Assert.Equal(NotificationState.On, listener.FocusState);
        Assert.Equal(
        [
            nameof(NotificationListener.FocusState), nameof(NotificationListener.NotificationMode),
            nameof(NotificationListener.DoNotDisturbState)
        ], changes);
        changes.Clear();

        // Both restrict, so this only changes the mode.
        listener.Apply(Snapshot() with { Mode = NotificationMode.PriorityOnly, Focus = NotificationState.On });
        Assert.Equal([nameof(NotificationListener.NotificationMode)], changes);
    }

    [Fact]
    public void Losing_access_empties_the_lists_and_says_why()
    {
        using var listener = Listener(new FakeBackend(), out _);
        listener.Apply(Snapshot(Toast(1)));

        listener.Apply(Snapshot() with { AccessStatus = NotificationAccessStatus.Denied });

        Assert.Empty(listener.Notifications);
        Assert.Empty(listener.Groups);
        Assert.Contains("denied", listener.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Dismissing_removes_the_notification_and_a_gone_one_cannot_be_dismissed()
    {
        var backend = new FakeBackend();
        using var listener = Listener(backend, out _);
        listener.Apply(Snapshot(Toast(1)));
        var notification = listener.Notifications[0];

        Assert.True(notification.Dismiss());

        Assert.Equal([1u], backend.Dismissed);
        Assert.Empty(listener.Notifications);
        Assert.Empty(listener.Groups);
        Assert.Equal("Hello", notification.Title);
        Assert.False(notification.Dismiss());
        Assert.Equal([1u], backend.Dismissed);
    }

    [Fact]
    public void A_failed_dismissal_stays_reports_why_and_clear_all_goes_on()
    {
        var backend = new FakeBackend { Refuse = 1 };
        using var listener = Listener(backend, out _);
        listener.Apply(Snapshot(Toast(1), Toast(2, minutes: 1), Toast(3, minutes: 2)));
        var failures = new List<string>();
        listener.OperationFailed += failures.Add;

        Assert.False(listener.ClearAll());

        Assert.Equal(1u, Assert.Single(listener.Notifications).Id);
        Assert.Equal("Refused", listener.Error);
        Assert.Equal(["Refused"], failures);
    }

    [Fact]
    public void A_group_dismisses_only_its_own_app()
    {
        var backend = new FakeBackend();
        using var listener = Listener(backend, out _);
        listener.Apply(Snapshot(Toast(1), Toast(2), Toast(3, "Chat")));
        var group = listener.Groups.Single(g => g.AppId == "Mail");

        Assert.True(group.Dismiss());

        Assert.Equal(3u, Assert.Single(listener.Notifications).Id);
        Assert.Equal("Chat", Assert.Single(listener.Groups).AppId);
    }

    [Fact]
    public async Task A_read_from_before_a_dismissal_does_not_bring_it_back()
    {
        var backend = new FakeBackend
        {
            Next = new TaskCompletionSource<NotificationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var listener = Listener(backend, out var loop);
        listener.Apply(Snapshot(Toast(1)));

        var read = listener.RefreshAsync();
        Assert.True(listener.Notifications[0].Dismiss());
        backend.Next.SetResult(Snapshot(Toast(1)));
        await read;
        loop.Process();

        Assert.Empty(listener.Notifications);
    }

    [Fact]
    public async Task A_read_after_access_is_requested_waits_and_a_refused_request_is_reported()
    {
        var backend = new FakeBackend { AccessError = new UnauthorizedAccessException("No") };
        using var listener = Listener(backend, out var loop);
        var failures = new List<string>();
        listener.OperationFailed += failures.Add;

        await listener.RequestAccessAsync();
        Assert.Equal(NotificationAccessStatus.Requesting, listener.AccessStatus);
        loop.Process();

        Assert.Equal(NotificationAccessStatus.Unavailable, listener.AccessStatus);
        Assert.Equal("No", listener.Error);
        Assert.Equal(["No"], failures);
    }

    [Fact]
    public async Task A_request_that_fails_in_a_way_nobody_expected_does_not_stop_later_reads()
    {
        var backend = new FakeBackend { AccessError = new FormatException("Odd") };
        using var listener = Listener(backend, out var loop);

        await listener.RequestAccessAsync();
        loop.Process();
        backend.Current = Snapshot(Toast(1));
        await listener.RefreshAsync();
        loop.Process();

        Assert.Equal(1u, Assert.Single(listener.Notifications).Id);
    }

    [Fact]
    public async Task A_disposed_listener_ignores_a_read_that_comes_late()
    {
        var backend = new FakeBackend();
        var listener = Listener(backend, out var loop);
        await listener.RefreshAsync();

        backend.Current = Snapshot(Toast(1));
        await listener.RefreshAsync();
        listener.Dispose();
        loop.Process();

        Assert.Empty(listener.Notifications);
        Assert.False(listener.ClearAll());
    }

    [Fact]
    public void Invoke_is_not_something_Windows_allows()
    {
        using var listener = Listener(new FakeBackend(), out _);
        listener.Apply(Snapshot(Toast(1)));
        var notification = listener.Notifications[0];

        Assert.False(notification.CanInvoke);
        Assert.False(notification.Invoke());
        Assert.Contains("Notification Center", listener.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_monitor_reads_at_once_and_stops_watching_when_disposed()
    {
        var backend = new FakeBackend { Current = Snapshot(Toast(1)) };
        var loop = new ManualEventLoop();
        var listener = new NotificationListener(loop, Log.Default, backend, true);

        await Wait.UntilAsync(() => listener.Notifications.Count == 1, "the first read", loop.Process);
        var monitor = listener.MonitorTask!;
        listener.Dispose();
        await monitor.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, backend.Unwatched);
    }

    [Fact]
    public async Task A_logo_arrives_after_the_notification_and_goes_with_it()
    {
        var logo = new FakeLogo(Png(2, 2));
        using var listener = Listener(new FakeBackend(), out var loop);
        var appId = "Mail." + Guid.NewGuid().ToString("N");

        listener.Apply(Snapshot(Toast(1, appId, logo: logo)));
        var notification = listener.Notifications[0];
        Assert.Equal("", notification.AppIcon);
        await Wait.UntilAsync(() => notification.AppIcon.Length > 0, "the logo", loop.Process);

        var path = new Uri(notification.AppIcon).LocalPath;
        Assert.True(File.Exists(path));
        Assert.EndsWith(".png", path, StringComparison.Ordinal);

        Assert.True(notification.Dismiss());
        Assert.Equal("", notification.AppIcon);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_logo_that_finishes_after_dismissal_leaves_no_file()
    {
        using var gate = new ManualResetEventSlim();
        var logo = new FakeLogo(Png(2, 2), gate);
        using var listener = Listener(new FakeBackend(), out var loop);
        var appId = "Mail." + Guid.NewGuid().ToString("N");
        var posted = 0;
        loop.Woken += () => Interlocked.Increment(ref posted);
        listener.Apply(Snapshot(Toast(1, appId, logo: logo)));
        var notification = listener.Notifications[0];
        await logo.Opened.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(notification.Dismiss());
        gate.Set();
        await Wait.UntilAsync(() => Volatile.Read(ref posted) > 0, "the read to end");
        loop.Process();

        Assert.Equal("", notification.AppIcon);
        var again = NotificationAppIcons.Read(appId, "Mail", new FakeLogo(null), Log.Default,
            TestContext.Current.CancellationToken);
        NotificationAppIcons.Release(again.Hold);
        Assert.Equal("", again.Url);
    }

    [Fact]
    public void A_toast_is_built_from_its_properties()
    {
        var sent = new List<(string AppId, string Xml, string Tag, string Group)>();
        var toast = new ToastMessage(new ManualEventLoop(),
            (appId, xml, tag, group, _) => sent.Add((appId, xml, tag, group)))
        {
            AppId = "Winshell",
            Title = "Hi <there>",
            Body = "Body",
            ActivationUri = "winshell://open",
            Tag = "t",
            Group = "g",
            Silent = true
        };
        toast.Actions.Add(new ToastAction { Text = "Open", ActivationUri = "winshell://action" });

        Assert.True(toast.Send());

        var (appId, xml, tag, group) = Assert.Single(sent);
        Assert.Equal(("Winshell", "t", "g"), (appId, tag, group));
        Assert.Contains("Hi &lt;there&gt;", xml, StringComparison.Ordinal);
        Assert.Contains("launch=\"winshell://open\"", xml, StringComparison.Ordinal);
        Assert.Contains("arguments=\"winshell://action\"", xml, StringComparison.Ordinal);
        Assert.Contains("silent=\"true\"", xml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Title", "", "", "")]
    [InlineData("Winshell", " ", "", "", "")]
    [InlineData("Winshell", "Title", "not a uri", "", "")]
    [InlineData("Winshell", "Title", "", "", "group without a tag")]
    public void A_toast_that_fails_validation_is_not_sent_and_says_why(string appId, string title, string uri,
        string tag, string group)
    {
        var loop = new ManualEventLoop();
        var sent = 0;
        var toast = new ToastMessage(loop, (_, _, _, _, _) => sent++)
            { AppId = appId, Title = title, ActivationUri = uri, Tag = tag, Group = group };
        var failures = new List<string>();
        toast.Failed += failures.Add;

        Assert.False(toast.Send());
        loop.Process();

        Assert.Equal(0, sent);
        Assert.Equal(toast.Error, Assert.Single(failures));
        Assert.NotEmpty(toast.Error);
    }

    [Fact]
    public void A_late_rejection_from_Windows_arrives_on_the_loop()
    {
        var loop = new ManualEventLoop();
        Action<string>? reject = null;
        var toast = new ToastMessage(loop, (_, _, _, _, failed) => reject = failed)
            { AppId = "Winshell", Title = "Hi" };
        Assert.True(toast.Send());

        reject!("Rejected");
        Assert.Empty(toast.Error);
        loop.Process();

        Assert.Equal("Rejected", toast.Error);
    }

    [Fact]
    public void A_toast_has_at_most_five_actions()
    {
        var toast = new ToastMessage(new ManualEventLoop(), (_, _, _, _, _) => { })
            { AppId = "Winshell", Title = "Hi" };
        for (var i = 0; i < 6; i++)
            toast.Actions.Add(new ToastAction { Text = "Open", ActivationUri = "winshell://open" });

        Assert.False(toast.Send());
    }

    private static byte[] Png(int width, int height)
    {
        byte[] data =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0,
            0, 0, 0, 0, 0, 0, 0
        ];
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    private sealed class FakeLogo(byte[]? data, ManualResetEventSlim? gate = null) : IAppLogoSource
    {
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Opened => _opened.Task;

        public Stream? Open(CancellationToken cancellation)
        {
            _opened.TrySetResult();
            gate?.Wait(cancellation);
            return data is null ? null : new MemoryStream(data);
        }
    }

    private sealed class FakeBackend : INotificationBackend
    {
        public NotificationSnapshot Current { get; set; } = Snapshot();

        public TaskCompletionSource<NotificationSnapshot>? Next { get; set; }

        public Exception? AccessError { get; init; }

        /// <summary>The id of the toast that cannot be dismissed.</summary>
        public uint Refuse { get; init; }

        public List<uint> Dismissed { get; } = [];

        public int Unwatched { get; private set; }

        public Task<NotificationAccessStatus> RequestAccessAsync()
        {
            return AccessError is { } error
                ? Task.FromException<NotificationAccessStatus>(error)
                : Task.FromResult(NotificationAccessStatus.Allowed);
        }

        public Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation)
        {
            return Next?.Task ?? Task.FromResult(Current);
        }

        public void Dismiss(uint id)
        {
            if (id == Refuse)
                throw new UnauthorizedAccessException("Refused");
            Dismissed.Add(id);
        }

        public Action? WatchToasts(Action changed)
        {
            return () => Unwatched++;
        }
    }
}
