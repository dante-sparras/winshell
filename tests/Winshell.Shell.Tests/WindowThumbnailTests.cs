using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Winshell.ShellTests;

/// <summary>The thumbnail can find the shell window it is drawn in. What DWM then paints is not asserted here.</summary>
public sealed class WindowThumbnailTests
{
    [Fact]
    public async Task A_thumbnail_finds_the_shell_window_it_is_in()
    {
        var found = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var thumbnail = new WindowThumbnail { Width = 80, Height = 48, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 40, screen.Y + 40, 200, 120),
                Content = new Grid { Children = { thumbnail } },
            };
            popup.Visible = true;
            await ShellFixture.LaidOut(thumbnail);
            var match = ShellWindow.Of(thumbnail);
            var missing = ShellWindow.Of(new Grid());
            return (Same: ReferenceEquals(match, popup), Missing: missing is null);
        });

        Assert.True(found.Same);
        Assert.True(found.Missing);
    }

    [Fact]
    public async Task A_thumbnail_still_finds_its_shell_window_after_a_collection()
    {
        var same = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var thumbnail = new WindowThumbnail { Width = 80, Height = 48, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 40, screen.Y + 40, 200, 120),
                Content = new Grid { Children = { thumbnail } },
            };
            popup.Visible = true;
            await ShellFixture.LaidOut(thumbnail);

            // The wrapper of a XamlRoot does not live as long as the native object it wraps.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return ReferenceEquals(ShellWindow.Of(thumbnail), popup);
        });

        Assert.True(same);
    }
}
