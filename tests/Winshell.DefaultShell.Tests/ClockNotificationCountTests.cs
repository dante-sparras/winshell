using System.Globalization;
using Winshell.DefaultShell.Taskbar;
using Winshell.Notifications;

namespace Winshell.DefaultShell.Tests;

/// <summary>
/// The clock says how many notifications the center is showing. The words stay on the accessible name: the
/// button still draws only the time and date.
/// </summary>
public sealed class ClockNotificationCountTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-clock-count-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_clock_names_the_notifications_the_center_is_showing()
    {
        var loop = new ManualEventLoop();
        var backend = new FakeBackend();
        using var listener = new NotificationListener(loop, Log.Default, backend);
        using var preferences = new Preferences(Path.Combine(_folder, "preferences.toml"), loop);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 15, 4, 5, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var center = new NotificationCenter(listener, preferences, time, CultureInfo.GetCultureInfo("en-US"));
        var clock = new TaskbarClockModel(time, preferences, CultureInfo.GetCultureInfo("en-US"));

        void Read() => clock.Refresh(center.Count);

        Read();
        AssertDrawnClockUnchanged(clock);
        Assert.DoesNotContain("notification", clock.AccessibleName, StringComparison.OrdinalIgnoreCase);

        listener.Apply(Snapshot(Toast(1, "mail", "Mail"), Toast(2, "mail", "Mail"), Toast(3, "chat", "Chat")));
        Read();
        Assert.Contains("3 notifications", clock.AccessibleName, StringComparison.Ordinal);
        AssertDrawnClockUnchanged(clock);

        center.ClearAll();
        Read();
        Assert.DoesNotContain("notification", clock.AccessibleName, StringComparison.OrdinalIgnoreCase);
        AssertDrawnClockUnchanged(clock);

        preferences.SetNotificationsFrom("mail", "Mail", false);
        listener.Apply(Snapshot(Toast(1, "mail", "Mail"), Toast(2, "mail", "Mail"), Toast(3, "chat", "Chat")));
        Read();
        Assert.Contains("1 notification", clock.AccessibleName, StringComparison.Ordinal);
        Assert.DoesNotContain("3 notifications", clock.AccessibleName, StringComparison.Ordinal);
        AssertDrawnClockUnchanged(clock);
        Assert.Equal(0, backend.Requests);
    }

    private static void AssertDrawnClockUnchanged(TaskbarClockModel clock)
    {
        Assert.Equal(2, clock.Text.Split('\n').Length);
        Assert.DoesNotContain("notification", clock.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static IncomingToast Toast(uint id, string appId, string appName) =>
        new(id, appId, appName, appName, "Body", new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero));

    private static NotificationSnapshot Snapshot(params IncomingToast[] toasts) =>
        new(NotificationAccessStatus.Allowed, toasts, NotificationState.Off, NotificationMode.Unrestricted);

    private sealed class FakeBackend : INotificationBackend
    {
        public int Requests { get; private set; }

        public Task<NotificationAccessStatus> RequestAccessAsync()
        {
            Requests++;
            return Task.FromResult(NotificationAccessStatus.Allowed);
        }

        public Task<NotificationSnapshot> ReadAsync() =>
            Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Unknown, NotificationMode.Unknown));

        public void Dismiss(uint id)
        {
        }
    }
}
