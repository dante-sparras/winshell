using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.ShellTests;

/// <summary>The thumbnail can find the shell window it is drawn in. What DWM then paints is not asserted here.</summary>
[Trait("Category", "Slow")]
public sealed class WindowThumbnailTests
{
    [Fact]
    public async Task A_thumbnail_finds_its_shell_window_after_a_collection_and_a_loose_control_finds_none()
    {
        var found = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var thumbnail = new WindowThumbnail
            {
                Width = 80, Height = 48, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            using var popup = new PopupWindow
            {
                Rect = new Rect(screen.X + 40, screen.Y + 40, 200, 120),
                Content = new Grid { Children = { thumbnail } }
            };
            popup.Visible = true;
            await ShellFixture.LaidOut(thumbnail);

            // The wrapper of a XamlRoot does not live as long as the native object it wraps.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return (Same: ReferenceEquals(ShellWindow.Of(thumbnail), popup),
                Missing: ShellWindow.Of(new Grid()) is null);
        });

        Assert.True(found.Same);
        Assert.True(found.Missing);
    }
}
