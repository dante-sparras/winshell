using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

[Collection(ThemeCollection.Name)]
public class PaletteLifetimeTests
{
    [Fact]
    public void Destroying_an_item_releases_its_palette()
    {
        var before = Theme.ListenerCount;
        var refs = UsePalette();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(before, Theme.ListenerCount);
        Assert.All(refs, reference => Assert.False(reference.IsAlive));
    }

    [Fact]
    public void A_live_palette_still_follows_the_theme()
    {
        var saved = Theme.Current;
        var item = new Item();
        try
        {
            Theme.Current = Theme.Default(false);
            var before = item.Palette.Window;
            var notes = 0;
            item.Palette.Active.Connect("windowChanged", _ => notes++);

            Theme.Current = Theme.Default(true);

            Assert.NotEqual(before, item.Palette.Window);
            Assert.Equal(1, notes);
        }
        finally
        {
            Theme.Current = saved;
            item.Destroy();
        }
    }

    private static WeakReference[] UsePalette()
    {
        var item = new Item();
        var palette = item.Palette;
        var refs = new[]
        {
            new WeakReference(palette),
            new WeakReference(palette.Active),
            new WeakReference(palette.Inactive),
            new WeakReference(palette.Disabled),
        };
        _ = palette.Window;
        item.Destroy();
        return refs;
    }
}
