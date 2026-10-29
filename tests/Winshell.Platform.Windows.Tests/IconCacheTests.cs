using SkiaSharp;
using Winshell.Platform.Windows;

namespace Winshell.Platform.Windows.Tests;

public class IconCacheTests
{
    [Fact]
    public void A_glyph_is_cached_a_miss_is_not_and_the_oldest_icon_is_dropped()
    {
        var provider = IconProvider.CreateForTests(2);
        var folder = provider.Load("folder", 32, 32);
        var again = provider.Load("folder", 32, 32);
        Assert.NotNull(folder);
        Assert.Same(folder, again);
        Assert.Equal(1, provider.CachedCount);

        Assert.Null(provider.Load("no-such-icon-zzz", 32, 32));
        Assert.Equal(1, provider.CachedCount);

        var document = provider.Load("document", 32, 32);
        Assert.NotNull(document);
        Assert.Equal(2, provider.CachedCount);
        var help = provider.Load("help", 32, 32);
        Assert.NotNull(help);
        Assert.Equal(IntPtr.Zero, folder.Handle);
        Assert.NotEqual(IntPtr.Zero, document.Handle);
        Assert.NotEqual(IntPtr.Zero, help.Handle);
        Assert.Equal(2, provider.CachedCount);

        provider.DropCache();
        Assert.Equal(0, provider.CachedCount);
        Assert.Equal(IntPtr.Zero, document.Handle);
        Assert.Equal(IntPtr.Zero, help.Handle);
        var redrawn = provider.Load("folder", 32, 32);
        Assert.NotNull(redrawn);
        Assert.NotSame(folder, redrawn);
        provider.DropCache();
    }
}
