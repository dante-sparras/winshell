using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Winshell.DefaultShell;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.ShellTests;

/// <summary>A quick settings tile, laid out in a real window with no quick settings around it.</summary>
[Trait("Category", "Slow")]
public sealed class QuickSettingsTileShellTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_tile_with_a_page_draws_its_chevron_beside_its_icon(bool toggle)
    {
        var (icon, chevron) = await ShellFixture.On(async shell =>
        {
            var tile = new QuickSettingsTile("powerMode", "Power mode", "", "", toggle, "", "powerMode");
            var view = new QuickSettingsTileView { Tile = tile };
            using var window = new FloatingWindow { Title = "Winshell test", Width = 200, Height = 120, Content = view };
            window.Visible = true;
            await ShellFixture.LaidOut(view);
            var glyphs = Icons(view).ToList();
            Assert.Equal(2, glyphs.Count);
            return (Bounds(glyphs[0], view), Bounds(glyphs[1], view));
        });

        Assert.True(chevron.Left >= icon.Right, $"The chevron starts at {chevron.Left}, inside the icon that ends at {icon.Right}.");
    }

    /// <summary>The icons the tile shows, in tree order: the tile's own, then the chevron.</summary>
    private static IEnumerable<FontIcon> Icons(DependencyObject root)
    {
        if (root is UIElement { Visibility: not Visibility.Visible })
            yield break;
        if (root is FontIcon icon)
            yield return icon;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var found in Icons(VisualTreeHelper.GetChild(root, i)))
                yield return found;
        }
    }

    private static Rect Bounds(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
}
