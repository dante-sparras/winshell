using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell;
using Winshell.DefaultShell.Taskbar;
using Winshell.Notifications;

namespace Winshell.ShellTests;

/// <summary>The notification center and calendar, laid out in a real window. No notification is read from Windows.</summary>
[Trait("Category", "Slow")]
public sealed class CalendarFlyoutShellTests
{
    [Fact]
    public async Task The_month_arrows_are_square_32_pixel_targets()
    {
        var directory = Directory.CreateTempSubdirectory("winshell-calendar-shell-").FullName;
        try
        {
            var sizes = await ShellFixture.On(async shell =>
            {
                var loop = new ManualEventLoop();
                using var listener = new NotificationListener(loop, shell.Log, new NoNotifications());
                using var preferences = new Preferences(Path.Combine(directory, "preferences.toml"), loop, shell.Log);
                var center = new NotificationCenter(listener, preferences, TimeProvider.System);
                var view = new CalendarFlyoutView(new CalendarMonth(TimeProvider.System), center, preferences);
                using var window = new FloatingWindow { Title = "Winshell test", Width = 384, Height = 780, Content = view };
                window.Visible = true;
                await ShellFixture.LaidOut(view);
                return new[] { "Previous month", "Next month" }.Select(name => Size(view, name)).ToArray();
            });

            Assert.All(sizes, size => Assert.Equal((32d, 32d), size));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static (double Width, double Height) Size(DependencyObject root, string name)
    {
        var button = Find(root, name) ?? throw new InvalidOperationException($"The calendar has no \"{name}\" button.");
        return (button.ActualWidth, button.ActualHeight);
    }

    private static Button? Find(DependencyObject root, string name)
    {
        if (root is Button button && AutomationProperties.GetName(button) == name)
            return button;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (Find(VisualTreeHelper.GetChild(root, i), name) is { } found)
                return found;
        }

        return null;
    }

    private sealed class NoNotifications : INotificationBackend
    {
        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);

        public Task<NotificationSnapshot> ReadAsync() =>
            Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Unknown, NotificationMode.Unknown));

        public void Dismiss(uint id)
        {
        }
    }
}
