using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        var sizes = await Opened(view => new[] { "Previous month", "Next month" }.Select(name => Size(view, name)).ToArray());

        Assert.All(sizes, size => Assert.Equal((32d, 32d), size));
    }

    [Fact]
    public async Task It_opens_on_one_Notifications_header_with_the_keyboard_on_the_flyout()
    {
        var (header, focused) = await Opened(view =>
        {
            view.FocusFlyout();
            return (FirstText(view), ReferenceEquals(FocusManager.GetFocusedElement(view.XamlRoot), view));
        });

        Assert.Equal("Notifications", header);
        Assert.True(focused);
    }

    /// <summary>The flyout in a small window, with a fixed clock and no notifications, and what <paramref name="read"/> reads from it.</summary>
    private static async Task<T> Opened<T>(Func<CalendarFlyoutView, T> read)
    {
        var directory = Directory.CreateTempSubdirectory("winshell-calendar-shell-").FullName;
        try
        {
            return await ShellFixture.On(async shell =>
            {
                var loop = new ManualEventLoop();
                var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
                using var listener = new NotificationListener(loop, shell.Log, new NoNotifications());
                using var preferences = new Preferences(Path.Combine(directory, "preferences.toml"), loop, shell.Log);
                var center = new NotificationCenter(listener, preferences, time);
                var view = new CalendarFlyoutView(new CalendarMonth(time), center, preferences);
                using var window = new FloatingWindow { Title = "Winshell test", Width = 320, Height = 480, Content = view };
                window.Visible = true;
                await ShellFixture.LaidOut(view);
                return read(view);
            });
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The first text shown, in reading order.</summary>
    private static string? FirstText(DependencyObject root)
    {
        if (root is TextBlock { Visibility: Visibility.Visible, Text.Length: > 0 } text)
            return text.Text;
        if (root is UIElement { Visibility: not Visibility.Visible })
            return null;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FirstText(VisualTreeHelper.GetChild(root, i)) is { } found)
                return found;
        }

        return null;
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

        public Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation) =>
            Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Unknown, NotificationMode.Unknown));

        public void Dismiss(uint id)
        {
        }
    }
}
