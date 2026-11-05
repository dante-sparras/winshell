using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Winshell.Native;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class WindowKindTests
{
    [Fact]
    public async Task A_popup_sits_where_it_was_asked()
    {
        var rect = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var content = new Grid();
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 60, screen.Y + 60, 200, 120),
                Content = content,
            };
            popup.Visible = true;
            await ShellFixture.LaidOut(content);
            Win32.GetWindowRect(popup.Host!.Handle, out var r);
            return (r.Width, r.Height, popup.Scale);
        });

        Assert.Equal(((int)(200 * rect.Scale), (int)(120 * rect.Scale)), (rect.Width, rect.Height));
    }

    [Fact]
    public async Task A_popup_that_grabs_focus_is_dismissed_by_a_click_outside_it()
    {
        var dismissed = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 60, screen.Y + 60, 200, 120),
                GrabFocus = true,
                Content = new Grid(),
            };
            var heard = 0;
            popup.Dismissed += () => heard++;
            popup.Visible = true;
            await Task.Yield();

            shell.Platform.OutsideClicks.Click(new POINT { X = (int)screen.X + 1000, Y = (int)screen.Y + 500 });

            return (heard, popup.Visible);
        });

        Assert.Equal((1, false), dismissed);
    }

    [Fact]
    public async Task A_click_inside_a_popup_that_grabs_focus_leaves_it_open()
    {
        var heard = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 60, screen.Y + 60, 200, 120),
                GrabFocus = true,
                Content = new Grid(),
            };
            var count = 0;
            popup.Dismissed += () => count++;
            popup.Visible = true;
            await Task.Yield();
            Win32.GetWindowRect(popup.Host!.Handle, out var r);

            shell.Platform.OutsideClicks.Click(new POINT { X = r.Left + 5, Y = r.Top + 5 });

            return count;
        });

        Assert.Equal(0, heard);
    }

    [Fact]
    public async Task A_click_on_a_flyout_that_extends_past_a_grabbing_popup_leaves_the_popup_open()
    {
        var after = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var button = new Button { Content = "Open", Width = 80, Height = 32 };
            var flyout = new Flyout { Content = new TextBlock { Text = "Flyout", Height = 300 }, ShouldConstrainToRootBounds = false, Placement = FlyoutPlacementMode.Bottom };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            flyout.Opened += (_, _) => opened.TrySetResult();
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 60, screen.Y + 60, 200, 48),
                GrabFocus = true,
                Content = button,
            };
            var heard = 0;
            popup.Dismissed += () => heard++;
            popup.Visible = true;
            await ShellFixture.LaidOut(button);
            flyout.ShowAt(button);
            await opened.Task;
            var child = (FrameworkElement)VisualTreeHelper.GetOpenPopupsForXamlRoot(button.XamlRoot)[0].Child;
            var corner = child.TransformToVisual(null).TransformPoint(default);
            var (left, _) = popup.Host!.Placement.MapToScreen(corner.X, corner.Y);
            Win32.GetWindowRect(popup.Host.Handle, out var r);

            var below = new POINT { X = left + 10, Y = r.Bottom + 10 };
            shell.Platform.OutsideClicks.Click(below);
            var result = (heard, popup.Visible, flyout.IsOpen);

            // A click outside both closes the flyout, so the mouse hook does not outlive the test.
            shell.Platform.OutsideClicks.Click(new POINT { X = (int)screen.Right - 5, Y = (int)screen.Bottom - 5 });
            return result;
        });

        Assert.Equal((0, true, true), after);
    }

    [Fact]
    public async Task A_floating_window_has_the_size_and_title_it_was_given()
    {
        var read = await ShellFixture.On(async _ =>
        {
            var content = new Grid();
            using var window = new FloatingWindow { Title = "Notes", Width = 500, Height = 300, Content = content };
            window.Visible = true;
            await ShellFixture.LaidOut(content);
            return (window.ActualWidth, window.ActualHeight, Win32Title(window.Host!.Handle));
        });

        Assert.Equal((500d, 300d, "Notes"), read);
    }

    [Fact]
    public async Task A_flyout_on_a_panel_that_never_activates_closes_on_a_click_outside_it()
    {
        var after = await ShellFixture.On(async shell =>
        {
            var button = new Button { Content = "Open", Width = 80, Height = 32 };
            var flyout = new Flyout { Content = new TextBlock { Text = "Flyout" }, ShouldConstrainToRootBounds = false, Placement = FlyoutPlacementMode.Bottom };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            flyout.Opened += (_, _) => opened.TrySetResult();
            using var panel = new PanelWindow { Anchors = Edges.Top | Edges.Left, Margins = new Thickness(40, 40, 0, 0), Width = 200, Height = 48, Content = button };
            panel.Visible = true;
            await ShellFixture.LaidOut(button);
            flyout.ShowAt(button);
            await opened.Task;
            var open = VisualTreeHelper.GetOpenPopupsForXamlRoot(button.XamlRoot).Count;

            var far = shell.Screens[0].Geometry;
            shell.Platform.OutsideClicks.Click(new POINT { X = (int)far.Right - 5, Y = (int)far.Bottom - 5 });

            return (open, flyout.IsOpen);
        });

        Assert.Equal((1, false), after);
    }

    private static string Win32Title(nint hwnd)
    {
        Span<char> text = stackalloc char[64];
        var length = PInvoke.GetWindowText((HWND)hwnd, text);
        return new string(text[..length]);
    }
}
