using System.Runtime.InteropServices;
using Windows.UI.Notifications.Management;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;
using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

public class NotificationTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Created = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000);
    private static IncomingToast Toast(uint id = 42, string appId = "Mail.App", int seconds = 0) =>
        new(id, appId, "Mail", "Hello", "A message", Created.AddSeconds(seconds));
    private static NotificationSnapshot Snapshot(params IncomingToast[] toasts) =>
        new(NotificationAccessStatus.Allowed, toasts, NotificationState.Off, NotificationMode.Unrestricted);
    private static ShellNotification Item(NotificationListener listener) => Assert.IsType<ShellNotification>(Assert.Single(listener.Notifications.Rows));

    [Fact]
    public void Snapshot_tracks_and_groups_all_toasts_newest_first_with_windows_identity_and_timestamps()
    {
        var listener = new NotificationListener(new FakeBackend());
        listener.Apply(Snapshot(Toast(1), Toast(2, seconds: 20), Toast(3, "Other.App", 10)));
        var rows = listener.Notifications.Rows.Cast<ShellNotification>().ToArray();
        Assert.Equal(new double[] { 2, 3, 1 }, rows.Select(n => n.Id));
        Assert.Equal("Mail.App", rows[0].AppId);
        Assert.Equal("Hello", rows[0].Title);
        Assert.Equal("A message", rows[0].Body);
        Assert.Equal(Created.AddSeconds(20).ToUnixTimeMilliseconds(), rows[0].CreatedAt);
        Assert.Equal(0, rows[0].ExpiresAt);
        var groups = listener.Groups.Rows.Cast<NotificationGroup>().ToArray();
        Assert.Equal(new[] { "Mail.App", "Other.App" }, groups.Select(g => g.AppId));
        Assert.Equal(new object?[] { rows[0], rows[2] }, groups[0].Notifications.Rows);
        listener.Destroy();
        Assert.All(rows, row => Assert.True(row.IsDestroyed));
        Assert.All(groups, group => Assert.True(group.IsDestroyed));
    }

    [Fact]
    public void Snapshot_updates_objects_in_place_and_removes_empty_groups()
    {
        var listener = new NotificationListener(new FakeBackend());
        var additions = 0;
        listener.Connect("notificationAdded", _ => additions++);
        listener.Apply(Snapshot(Toast()));
        var item = Item(listener);
        var group = Assert.IsType<NotificationGroup>(Assert.Single(listener.Groups.Rows));
        var changes = 0;
        item.Connect("titleChanged", _ => changes++);
        listener.Apply(Snapshot(Toast() with { Title = "Updated", ExpiresAt = Created.AddHours(1) }));
        Assert.Same(item, Item(listener));
        Assert.Same(group, Assert.Single(listener.Groups.Rows));
        Assert.Equal("Updated", item.Title);
        Assert.Equal(Created.AddHours(1).ToUnixTimeMilliseconds(), item.ExpiresAt);
        Assert.Equal(1, additions);
        Assert.Equal(1, changes);
        listener.Apply(Snapshot());
        Assert.True(item.IsDestroyed);
        Assert.True(group.IsDestroyed);
        Assert.Empty(listener.Groups.Rows);
    }

    [Fact]
    public void Missing_app_identity_does_not_merge_unrelated_toasts_and_duplicate_ids_are_ignored()
    {
        var listener = new NotificationListener(new FakeBackend());
        listener.Apply(Snapshot(Toast(1, ""), Toast(2, ""), Toast(2, "")));
        Assert.Equal(2, listener.Notifications.Rows.Count);
        Assert.Equal(2, listener.Groups.Rows.Count);
    }

    [Fact]
    public void A_changed_app_identity_moves_the_existing_notification_to_its_new_group()
    {
        var listener = new NotificationListener(new FakeBackend());
        listener.Apply(Snapshot(Toast()));
        var item = Item(listener);
        var oldGroup = Assert.IsType<NotificationGroup>(Assert.Single(listener.Groups.Rows));
        listener.Apply(Snapshot(Toast() with { AppId = "New.App", AppName = "New app" }));
        Assert.Same(item, Item(listener));
        Assert.True(oldGroup.IsDestroyed);
        var group = Assert.IsType<NotificationGroup>(Assert.Single(listener.Groups.Rows));
        Assert.Equal("New app", group.AppName);
        Assert.Same(item, Assert.Single(group.Notifications.Rows));
    }

    [Fact]
    public void Dismiss_removes_from_windows_and_models_once()
    {
        var backend = new FakeBackend();
        var listener = new NotificationListener(backend);
        listener.Apply(Snapshot(Toast(uint.MaxValue)));
        var item = Item(listener);
        Assert.Equal((double)uint.MaxValue, item.Id);
        Assert.True(item.Dismiss());
        Assert.False(item.Dismiss());
        Assert.Equal(new[] { uint.MaxValue }, backend.Dismissed);
        Assert.True(item.IsDestroyed);
        Assert.Empty(listener.Notifications.Rows);
        Assert.Empty(listener.Groups.Rows);
    }

    [Fact]
    public void Failed_dismissal_keeps_the_item_and_reports_the_error()
    {
        var listener = new NotificationListener(new FakeBackend { DismissError = new UnauthorizedAccessException("Access revoked") });
        listener.Apply(Snapshot(Toast()));
        var item = Item(listener);
        string? error = null;
        listener.Connect("operationFailed", args => error = (string)args[0]!);
        Assert.False(item.Dismiss());
        Assert.Same(item, Item(listener));
        Assert.False(item.IsDestroyed);
        Assert.Equal("Access revoked", error);
    }

    [Fact]
    public void Unsupported_activation_is_explicit_and_does_not_dismiss_or_launch_an_app()
    {
        var backend = new FakeBackend();
        var listener = new NotificationListener(backend);
        listener.Apply(Snapshot(Toast()));
        var item = Item(listener);
        Assert.False(item.CanInvoke);
        Assert.False(item.Invoke());
        Assert.Contains("does not support invoking", listener.Error);
        Assert.Same(item, Item(listener));
        Assert.Empty(backend.Dismissed);
    }

    [Theory]
    [InlineData(NotificationAccessStatus.Unspecified)]
    [InlineData(NotificationAccessStatus.Denied)]
    [InlineData(NotificationAccessStatus.Unavailable)]
    public void Losing_access_clears_private_content_and_destroys_objects(NotificationAccessStatus status)
    {
        var listener = new NotificationListener(new FakeBackend());
        listener.Apply(Snapshot(Toast()));
        var item = Item(listener);
        listener.Apply(new(status, [Toast()], NotificationState.On, NotificationMode.PriorityOnly));
        Assert.Empty(listener.Notifications.Rows);
        Assert.Empty(listener.Groups.Rows);
        Assert.True(item.IsDestroyed);
        Assert.Equal((int)status, listener.AccessStatus);
        Assert.Equal((int)NotificationState.On, listener.FocusState);
    }

    [Theory]
    [InlineData(NotificationAccessStatus.Allowed)]
    [InlineData(NotificationAccessStatus.Denied)]
    [InlineData(NotificationAccessStatus.Unspecified)]
    public async Task Permission_request_starts_on_calling_thread_and_surfaces_result(NotificationAccessStatus result)
    {
        var backend = new FakeBackend { Access = Task.FromResult(result) };
        var listener = new NotificationListener(backend);
        Assert.Equal(0, backend.Requests);
        var thread = Environment.CurrentManagedThreadId;
        await listener.RequestAccessAsync();
        Assert.Equal(thread, backend.RequestThread);
        Assert.Equal((int)result, listener.AccessStatus);
        Assert.Equal(1, backend.Requests);
        Assert.Equal(result == NotificationAccessStatus.Denied, listener.Error.Length > 0);
    }

    [Fact]
    public async Task Duplicate_permission_requests_are_coalesced_and_cancellation_can_be_retried()
    {
        var completion = new TaskCompletionSource<NotificationAccessStatus>();
        var backend = new FakeBackend { Access = completion.Task };
        var listener = new NotificationListener(backend);
        var request = listener.RequestAccessAsync();
        Assert.Equal((int)NotificationAccessStatus.Requesting, listener.AccessStatus);
        await listener.RequestAccessAsync();
        Assert.Equal(1, backend.Requests);
        completion.SetResult(NotificationAccessStatus.Unspecified);
        await request;
        await listener.RequestAccessAsync();
        Assert.Equal(2, backend.Requests);
    }

    [Fact]
    public async Task Permission_failure_becomes_unavailable_without_throwing()
    {
        var listener = new NotificationListener(new FakeBackend
        {
            Access = Task.FromException<NotificationAccessStatus>(new COMException("No package capability")),
        });
        await listener.RequestAccessAsync();
        Assert.Equal((int)NotificationAccessStatus.Unavailable, listener.AccessStatus);
        Assert.Equal("No package capability", listener.Error);
    }

    [Fact]
    public async Task A_snapshot_in_flight_cannot_resurrect_a_dismissed_toast()
    {
        var completion = new TaskCompletionSource<NotificationSnapshot>();
        var backend = new FakeBackend { Read = completion.Task };
        var listener = new NotificationListener(backend);
        listener.Apply(Snapshot(Toast()));
        var refresh = listener.RefreshAsync();
        Assert.True(Item(listener).Dismiss());
        completion.SetResult(Snapshot(Toast()));
        await refresh;
        Assert.Empty(listener.Notifications.Rows);
    }

    [Fact]
    public async Task A_snapshot_in_flight_cannot_override_a_permission_decision_or_mutate_a_destroyed_listener()
    {
        var completion = new TaskCompletionSource<NotificationSnapshot>();
        var backend = new FakeBackend { Read = completion.Task, Access = Task.FromResult(NotificationAccessStatus.Denied) };
        var listener = new NotificationListener(backend);
        var refresh = listener.RefreshAsync();
        await listener.RequestAccessAsync();
        completion.SetResult(Snapshot(Toast()));
        await refresh;
        Assert.Equal((int)NotificationAccessStatus.Denied, listener.AccessStatus);
        Assert.Empty(listener.Notifications.Rows);
        listener.Destroy();
        listener.Apply(Snapshot(Toast()));
        await listener.RequestAccessAsync();
        Assert.Empty(listener.Notifications.Rows);
        Assert.Equal(1, backend.Requests);
    }

    [Fact]
    public async Task Snapshot_failure_clears_content_but_a_later_read_recovers()
    {
        var backend = new FakeBackend { Read = Task.FromException<NotificationSnapshot>(new COMException("Disconnected")) };
        var listener = new NotificationListener(backend);
        listener.Apply(Snapshot(Toast()));
        await listener.RefreshAsync();
        Assert.Empty(listener.Notifications.Rows);
        Assert.Equal((int)NotificationAccessStatus.Unavailable, listener.AccessStatus);
        Assert.Equal("Disconnected", listener.Error);
        backend.Read = Task.FromResult(Snapshot(Toast()));
        await listener.RefreshAsync();
        Assert.Equal("", listener.Error);
        Assert.Equal((int)NotificationAccessStatus.Allowed, listener.AccessStatus);
        Assert.Equal("Hello", Item(listener).Title);
    }

    [Theory]
    [InlineData(NotificationMode.Unknown, NotificationState.Unknown)]
    [InlineData(NotificationMode.Unrestricted, NotificationState.Off)]
    [InlineData(NotificationMode.PriorityOnly, NotificationState.On)]
    [InlineData(NotificationMode.AlarmsOnly, NotificationState.On)]
    public void Focus_and_do_not_disturb_are_separate_read_only_states(NotificationMode mode, NotificationState dnd)
    {
        var listener = new NotificationListener(new FakeBackend());
        listener.Apply(new(NotificationAccessStatus.Denied, [], NotificationState.Off, mode));
        Assert.Equal((int)dnd, listener.DoNotDisturbState);
        Assert.Equal((int)NotificationState.Off, listener.FocusState);
        Assert.False(listener.Meta.Properties["focusState"].CanWrite);
        Assert.False(listener.Meta.Properties["notificationMode"].CanWrite);
        Assert.False(listener.Meta.Properties["doNotDisturbState"].CanWrite);
        Assert.False(listener.HasMember("trackedNotifications"));
        Assert.False(listener.HasMember("bodySupported"));
    }

    [Theory]
    [InlineData(0, NotificationMode.Unrestricted)]
    [InlineData(1, NotificationMode.PriorityOnly)]
    [InlineData(2, NotificationMode.AlarmsOnly)]
    [InlineData(19, NotificationMode.Unknown)]
    public void Native_notification_mode_mapping_preserves_unknown_values(int native, NotificationMode expected) =>
        Assert.Equal(expected, WindowsNotificationBackend.MapMode(native));

    [Theory]
    [InlineData(UserNotificationListenerAccessStatus.Allowed, NotificationAccessStatus.Allowed)]
    [InlineData(UserNotificationListenerAccessStatus.Denied, NotificationAccessStatus.Denied)]
    [InlineData(UserNotificationListenerAccessStatus.Unspecified, NotificationAccessStatus.Unspecified)]
    public void Native_access_mapping_preserves_prompt_results(UserNotificationListenerAccessStatus native, NotificationAccessStatus expected) =>
        Assert.Equal(expected, WindowsNotificationBackend.MapAccess(native));

    [Fact]
    public async Task Native_read_is_safe_without_prompting_or_changing_settings()
    {
        var snapshot = await new WindowsNotificationBackend().ReadAsync();
        Assert.True(Enum.IsDefined(snapshot.AccessStatus));
        Assert.True(Enum.IsDefined(snapshot.Focus));
        Assert.True(Enum.IsDefined(snapshot.Mode));
        if (snapshot.AccessStatus != NotificationAccessStatus.Allowed)
            Assert.Empty(snapshot.Notifications);
        output.WriteLine($"Native read: access={snapshot.AccessStatus}, focus={snapshot.Focus}, mode={snapshot.Mode}");
    }

    [Fact]
    public async Task Snapshot_changes_are_posted_to_the_engine_loop_and_dropped_after_destruction()
    {
        var backend = new FakeBackend { Read = Task.FromResult(Snapshot(Toast())) };
        var listener = new NotificationListener(backend);
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        listener.Engine = engine;
        await listener.RefreshAsync();
        Assert.Empty(listener.Notifications.Rows);
        loop.Process();
        Assert.Equal("Hello", Item(listener).Title);
        backend.Read = Task.FromResult(Snapshot(Toast() with { Title = "Too late" }));
        await listener.RefreshAsync();
        listener.Destroy();
        loop.Process();
        Assert.Equal("Hello", Item(listener).Title);
    }

    [Fact]
    public void Qml_bindings_follow_the_model_content_and_read_only_states()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-notifications", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var listener = new NotificationListener(new FakeBackend());
        try
        {
            var path = Path.Combine(directory, "shell.qml");
            File.WriteAllText(path, """
                import QtQml
                import Winshell.Notifications
                QtObject {
                    property int status: TestCenter.accessStatus
                    property int count: TestCenter.notifications.values.length
                    property string title: count > 0 ? TestCenter.notifications.values[0].title : ""
                    property int dnd: TestCenter.doNotDisturbState
                    property bool denied: status === NotificationAccessStatus.Denied
                }
                """);
            var registry = StandardModules.Create();
            NotificationModule.Register(registry);
            registry.Module("Winshell.Notifications").Singleton(_ => listener, "TestCenter");
            var loop = new ManualEventLoop();
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            var root = engine.CreateRoot(path);
            listener.Apply(Snapshot(Toast()));
            loop.Process();
            Assert.Equal(1, root.ReadProperty("count"));
            Assert.Equal("Hello", root.ReadProperty("title"));
            listener.Apply(Snapshot(Toast() with { Title = "Updated" }));
            loop.Process();
            Assert.Equal("Updated", root.ReadProperty("title"));
            listener.Apply(new(NotificationAccessStatus.Denied, [], NotificationState.On, NotificationMode.PriorityOnly));
            loop.Process();
            Assert.Equal(0, root.ReadProperty("count"));
            Assert.Equal("", root.ReadProperty("title"));
            Assert.True((bool)root.ReadProperty("denied")!);
            Assert.Equal((int)NotificationState.On, root.ReadProperty("dnd"));
        }
        finally { listener.Destroy(); Directory.Delete(directory, recursive: true); }
    }

    private sealed class FakeBackend : INotificationBackend
    {
        public Task<NotificationAccessStatus> Access { get; set; } = Task.FromResult(NotificationAccessStatus.Allowed);
        public Task<NotificationSnapshot> Read { get; set; } = Task.FromResult(Snapshot());
        public Exception? DismissError { get; init; }
        public List<uint> Dismissed { get; } = [];
        public int Requests { get; private set; }
        public int RequestThread { get; private set; }
        public Task<NotificationAccessStatus> RequestAccessAsync()
        {
            Requests++;
            RequestThread = Environment.CurrentManagedThreadId;
            return Access;
        }
        public Task<NotificationSnapshot> ReadAsync() => Read;
        public void Dismiss(uint id)
        {
            if (DismissError is { } error) throw error;
            Dismissed.Add(id);
        }
    }
}
