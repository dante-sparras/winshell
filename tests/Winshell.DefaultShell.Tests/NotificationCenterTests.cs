using System.Globalization;
using Winshell.DefaultShell.Taskbar;
using Winshell.Notifications;

namespace Winshell.DefaultShell.Tests;

public sealed class NotificationCenterTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-center-").FullName;
    private int _files;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_notification_from_today_is_the_time_and_one_from_yesterday_adds_the_weekday()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 22, 30, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(zone);
        using var open = Open(time, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("1:00 AM", open.Center.NotificationTime(new DateTimeOffset(2026, 10, 28, 23, 0, 0, TimeSpan.Zero)));
        Assert.Equal("12:00 AM", open.Center.NotificationTime(new DateTimeOffset(2026, 10, 28, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Wed 11:59 PM", open.Center.NotificationTime(new DateTimeOffset(2026, 10, 28, 21, 59, 0, TimeSpan.Zero)));
        Assert.Equal("Wed 11:00 PM", open.Center.NotificationTime(new DateTimeOffset(2026, 10, 28, 21, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Hidden_apps_stay_out_of_the_list_the_count_and_clear_all()
    {
        using var open = Open();
        open.Listener.Apply(Snapshot(Toast(1, "mail", "Mail"), Toast(2, "mail", "Mail", minutes: 1), Toast(3, "chat", "Chat", minutes: 2)));
        var chat = open.Center.VisibleGroups.Single(group => group.AppId == "chat");

        Assert.Equal(3, open.Center.Count);
        Assert.Equal(["chat", "mail"], open.Center.VisibleGroups.Select(group => group.AppId));
        Assert.True(open.Center.CanClear);
        Assert.False(open.Center.ShowCaughtUp);

        open.Preferences.SetNotificationsFrom("mail", "Mail", false);
        Assert.Same(chat, Assert.Single(open.Center.VisibleGroups));
        Assert.Equal(1, open.Center.Count);

        open.Center.ClearAll();
        Assert.Equal(0, open.Center.Count);
        Assert.Empty(open.Center.VisibleGroups);
        Assert.Equal([2u, 1u], open.Listener.Notifications.Select(notification => notification.Id));
        Assert.Equal([3u], open.Backend.Dismissed);
        Assert.False(open.Center.CanClear);
        Assert.True(open.Center.ShowCaughtUp);
    }

    [Fact]
    public void Clear_on_a_group_and_dismiss_on_one_notification_leave_the_rest()
    {
        using var open = Open();
        open.Listener.Apply(Snapshot(Toast(1, "mail", "Mail"), Toast(2, "mail", "Mail", minutes: 1), Toast(3, "chat", "Chat", minutes: 2)));
        var mail = open.Center.VisibleGroups.Single(group => group.AppId == "mail");
        var kept = mail.Notifications.Single(notification => notification.Id == 1);

        kept.Dismiss();
        Assert.Same(mail, open.Center.VisibleGroups.Single(group => group.AppId == "mail"));
        Assert.Equal([1u], open.Backend.Dismissed);
        Assert.Equal(2, open.Center.Count);

        mail.Dismiss();
        Assert.Equal(["chat"], open.Center.VisibleGroups.Select(group => group.AppId));
        Assert.Equal(1, open.Center.Count);
        Assert.Equal([1u, 2u], open.Backend.Dismissed);
    }

    [Fact]
    public void An_app_with_no_name_is_unknown_and_clear_still_uses_the_raw_name()
    {
        using var open = Open();
        open.Listener.Apply(Snapshot(new IncomingToast(7, "App.NoName", "", "Hello", "Body", new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero))));
        var group = Assert.Single(open.Center.VisibleGroups);

        Assert.Equal("", group.AppName);
        Assert.Equal("Unknown app", NotificationCenter.AppName(group.AppName));
        Assert.Equal("Clear notifications from ", NotificationCenter.ClearName(group.AppName));
        Assert.Equal("Mail", NotificationCenter.AppName("Mail"));
        Assert.Equal("Clear notifications from Mail", NotificationCenter.ClearName("Mail"));
        Assert.Equal("Dismiss Hello", NotificationCenter.DismissName(group.Notifications[0].Title));
    }

    [Fact]
    public void Before_the_first_read_access_is_unspecified_and_nothing_has_asked()
    {
        using var open = Open();

        Assert.Equal(NotificationAccessStatus.Unspecified, open.Listener.AccessStatus);
        Assert.Equal("Do not disturb: Unknown \u00b7 Focus: Unknown", open.Center.StatusLine);
        Assert.True(open.Center.ShowAllow);
        Assert.Equal("Allow notification access", open.Center.AllowText);
        Assert.False(open.Center.ShowWaiting);
        Assert.False(open.Center.ShowError);
        Assert.False(open.Center.ShowCaughtUp);
        Assert.Equal(0, open.Backend.Requests);
    }

    [Fact]
    public void The_allow_button_is_what_asks_and_waiting_shows_until_the_answer_arrives()
    {
        using var open = Open();
        open.Listener.Apply(new NotificationSnapshot(NotificationAccessStatus.Denied, [], NotificationState.Off, NotificationMode.Unrestricted));
        Assert.True(open.Center.ShowError);

        open.Center.RequestAccess();

        Assert.Equal(1, open.Backend.Requests);
        Assert.Equal(NotificationAccessStatus.Requesting, open.Listener.AccessStatus);
        Assert.True(open.Center.ShowWaiting);
        Assert.Equal("Waiting for Windows permission\u2026", open.Center.WaitingText);
        Assert.False(open.Center.ShowAllow);
        Assert.False(open.Center.ShowError);
        Assert.Equal("", open.Center.Error);

        open.Loop.Process();

        Assert.Equal(NotificationAccessStatus.Allowed, open.Listener.AccessStatus);
        Assert.False(open.Center.ShowWaiting);
        Assert.True(open.Center.ShowCaughtUp);
        Assert.Equal("You're all caught up", open.Center.CaughtUpText);
        Assert.Equal(1, open.Backend.Requests);
    }

    [Fact]
    public void Denied_and_unavailable_show_the_listener_error_and_not_the_empty_state()
    {
        using var open = Open();

        open.Listener.Apply(new NotificationSnapshot(NotificationAccessStatus.Denied, [], NotificationState.Off, NotificationMode.Unrestricted));
        Assert.Equal("Notification access was denied. Allow access in Windows Settings to use the listener.", open.Center.Error);
        Assert.True(open.Center.ShowError);
        Assert.False(open.Center.ShowAllow);
        Assert.False(open.Center.ShowWaiting);
        Assert.False(open.Center.ShowCaughtUp);

        open.Listener.Apply(new NotificationSnapshot(NotificationAccessStatus.Unavailable, [], NotificationState.Unknown, NotificationMode.Unknown));
        Assert.Equal("Notification access is unavailable. A package identity with the userNotificationListener capability may be required.", open.Center.Error);
        Assert.True(open.Center.ShowError);
        Assert.False(open.Center.ShowCaughtUp);
        Assert.Equal("Do not disturb: Unknown \u00b7 Focus: Unknown", open.Center.StatusLine);
    }

    [Fact]
    public void An_allowed_read_can_show_its_error_and_the_empty_state_together()
    {
        using var open = Open();

        open.Listener.Apply(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Off, NotificationMode.PriorityOnly, "The tray read failed"));

        Assert.Equal("The tray read failed", open.Center.Error);
        Assert.True(open.Center.ShowError);
        Assert.True(open.Center.ShowCaughtUp);
        Assert.Equal("Do not disturb: On \u00b7 Focus: Off", open.Center.StatusLine);
    }

    [Theory]
    [InlineData(NotificationMode.Unrestricted, NotificationState.Off, "Off", "Off")]
    [InlineData(NotificationMode.PriorityOnly, NotificationState.Off, "On", "Off")]
    [InlineData(NotificationMode.AlarmsOnly, NotificationState.On, "On", "On")]
    [InlineData(NotificationMode.Unknown, NotificationState.Unknown, "Unknown", "Unknown")]
    public void The_status_line_names_do_not_disturb_and_focus(NotificationMode mode, NotificationState focus, string disturb, string focusWord)
    {
        using var open = Open();

        open.Listener.Apply(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], focus, mode));

        Assert.Equal("Do not disturb: " + disturb + " \u00b7 Focus: " + focusWord, open.Center.StatusLine);
    }

    [Fact]
    public void The_clock_minute_changes_when_the_local_minute_does()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 30, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var open = Open(time);
        open.Center.NoteClock();
        var minute = open.Center.Clock;

        time.Advance(TimeSpan.FromSeconds(20));
        open.Center.NoteClock();
        Assert.Equal(minute, open.Center.Clock);

        time.Advance(TimeSpan.FromSeconds(20));
        open.Center.NoteClock();
        Assert.Equal("2026-11-02 12:01", open.Center.Clock);
    }

    private Opened Open(TimeProvider? time = null, CultureInfo? culture = null)
    {
        var loop = new ManualEventLoop();
        var backend = new FakeBackend();
        var listener = new NotificationListener(loop, Log.Default, backend);
        var preferences = new Preferences(Path.Combine(_folder, $"preferences-{_files++}.toml"), loop);
        var center = new NotificationCenter(listener, preferences, time ?? TimeProvider.System, culture);
        return new Opened(listener, preferences, center, loop, backend);
    }

    private static IncomingToast Toast(uint id, string appId, string appName, int minutes = 0) =>
        new(id, appId, appName, appName, "Body", new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero).AddMinutes(minutes));

    private static NotificationSnapshot Snapshot(params IncomingToast[] toasts) =>
        new(NotificationAccessStatus.Allowed, toasts, NotificationState.Off, NotificationMode.Unrestricted);

    private sealed class Opened(NotificationListener listener, Preferences preferences, NotificationCenter center, ManualEventLoop loop, FakeBackend backend) : IDisposable
    {
        public NotificationListener Listener { get; } = listener;
        public Preferences Preferences { get; } = preferences;
        public NotificationCenter Center { get; } = center;
        public ManualEventLoop Loop { get; } = loop;
        public FakeBackend Backend { get; } = backend;
        public void Dispose()
        {
            Listener.Dispose();
            Preferences.Dispose();
        }
    }

    private sealed class FakeBackend : INotificationBackend
    {
        public int Requests { get; private set; }

        public List<uint> Dismissed { get; } = [];

        public Task<NotificationAccessStatus> RequestAccessAsync()
        {
            Requests++;
            return Task.FromResult(NotificationAccessStatus.Allowed);
        }

        public Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation) =>
            Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Unknown, NotificationMode.Unknown));

        public void Dismiss(uint id) => Dismissed.Add(id);
    }
}
