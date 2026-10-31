using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// The clock says how many notifications the center is showing. The words stay on the accessible name: the
/// button still draws only the time and date, which the taskbar snapshots compare.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class ClockNotificationCountTests
{
    [Fact]
    public void The_clock_names_the_notifications_the_center_is_showing()
    {
        using var shell = new DefaultShellHarness("""
            import Winshell
            TaskbarClock { }
            """);
        var listener = new NotificationListener(new FakeBackend()) { Engine = shell.Engine };
        try
        {
            Assert.Equal("Open notification center and calendar", Clock(shell).Description);
            Assert.DoesNotContain("notification", Clock(shell).Name, StringComparison.OrdinalIgnoreCase);
            AssertDrawnClockUnchanged(shell);

            shell.Root.WriteProperty("notificationListener", listener);
            listener.Apply(Snapshot(Toast(1, "mail", "Mail"), Toast(2, "mail", "Mail"), Toast(3, "chat", "Chat")));
            shell.Settle();
            Assert.Contains("3 notifications", Clock(shell).Name, StringComparison.Ordinal);
            AssertDrawnClockUnchanged(shell);

            Assert.True(listener.ClearAll());
            shell.Settle();
            Assert.DoesNotContain("notification", Clock(shell).Name, StringComparison.OrdinalIgnoreCase);

            var preferences = shell.Engine.GetFileSingleton(Path.Combine(shell.Directory, "Preferences.qml"));
            Assert.Equal("", preferences.ReadProperty("path"));
            shell.Engine.Js.CallFunction(preferences.GetFunction("setNotificationsFrom")!, shell.Engine.Js.Wrap(preferences), ["mail", "Mail", false]);
            listener.Apply(Snapshot(Toast(1, "mail", "Mail"), Toast(2, "mail", "Mail"), Toast(3, "chat", "Chat")));
            shell.Settle();
            Assert.Contains("1 notification", Clock(shell).Name, StringComparison.Ordinal);
            Assert.DoesNotContain("3 notifications", Clock(shell).Name, StringComparison.Ordinal);
            AssertDrawnClockUnchanged(shell);
            Assert.Empty(shell.Messages);
        }
        finally
        {
            listener.Destroy();
        }
    }

    private static void AssertDrawnClockUnchanged(DefaultShellHarness shell)
    {
        var text = (string)shell.Find("clockButton").ReadProperty("text")!;
        Assert.Equal(2, text.Split('\n').Length);
        Assert.DoesNotContain("notification", text, StringComparison.OrdinalIgnoreCase);
    }

    private static AccessibleNode Clock(DefaultShellHarness shell)
    {
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        return Assert.Single(Nodes(tree.Root), n => n.Description == "Open notification center and calendar");
    }

    private static IEnumerable<AccessibleNode> Nodes(AccessibleNode node) =>
        node.Children.SelectMany(n => new[] { n }.Concat(Nodes(n)));

    private static IncomingToast Toast(uint id, string appId, string appName) =>
        new(id, appId, appName, appName, "Body", new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero));

    private static NotificationSnapshot Snapshot(params IncomingToast[] toasts) =>
        new(NotificationAccessStatus.Allowed, toasts, NotificationState.Off, NotificationMode.Unrestricted);

    private sealed class FakeBackend : INotificationBackend
    {
        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);

        public Task<NotificationSnapshot> ReadAsync() =>
            Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Unknown, NotificationMode.Unknown));

        public void Dismiss(uint id)
        {
        }
    }
}
