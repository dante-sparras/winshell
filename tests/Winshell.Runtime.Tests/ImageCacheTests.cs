using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ImageCacheTests
{
    [Fact]
    public void A_missing_file_is_loaded_once_it_exists()
    {
        ImageLoader.ClearCache();
        using var dir = new TempDir();
        var path = dir.File("later.png");
        Assert.Null(ImageLoader.Load(path, null, 0, 0));
        WritePng(path, SKColors.Green);
        using var image = ImageLoader.Load(path, null, 0, 0);
        Assert.Equal(SKColors.Green, Pixel(image));
    }

    [Fact]
    public void A_rewritten_file_is_loaded_again_and_the_old_image_stays()
    {
        ImageLoader.ClearCache();
        using var dir = new TempDir();
        var path = dir.File("wall.png");
        WritePng(path, SKColors.Red);
        using var first = ImageLoader.Load(path, null, 0, 0);
        WritePng(path, SKColors.Blue);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        using var second = ImageLoader.Load(path, null, 0, 0);
        Assert.Equal(SKColors.Blue, Pixel(second));
        Assert.Equal(SKColors.Red, Pixel(first));
    }

    [Fact]
    public void The_cache_drops_images_past_its_limit_without_disposing_the_caller()
    {
        ImageLoader.ClearCache();
        using var dir = new TempDir();
        var path = dir.File("kept.png");
        WritePng(path, SKColors.Red);
        using var kept = ImageLoader.Load(path, null, 0, 0);
        for (var i = 0; i < 40; i++)
        {
            var extra = dir.File($"extra-{i}.png");
            WritePng(extra, new SKColor((byte)i, 0, 0));
            using var image = ImageLoader.Load(extra, null, 0, 0);
            Assert.NotNull(image);
        }
        Assert.InRange(ImageLoader.CachedCount, 1, 32);
        Assert.Equal(SKColors.Red, Pixel(kept));
    }

    [Fact]
    public void Provider_and_data_images_are_not_cached_and_a_theme_change_reloads_the_provider()
    {
        ImageLoader.ClearCache();
        var before = ImageLoader.CachedCount;
        var probe = new Probe();
        ImageLoader.AddProvider("cache-probe", probe);
        using var provided = ImageLoader.Load("image://cache-probe/one", null, 2, 2);
        using var again = ImageLoader.Load("image://cache-probe/one", null, 2, 2);
        using var inline = ImageLoader.Load(DataUrl(SKColors.Yellow), null, 0, 0);
        Assert.Equal(2, probe.Calls);
        Assert.NotNull(inline);
        Assert.Equal(before, ImageLoader.CachedCount);

        var saved = Theme.Current;
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            Image { objectName: "pic"; source: "image://cache-probe/one"; width: 2; height: 2 }
            """));
        try
        {
            var pic = (Image)shell.Load();
            Assert.Equal(SKColors.Red, Pixel(pic.CurrentImage));
            probe.Color = SKColors.Blue;
            Theme.Current = Theme.Default(!saved.IsDark);
            Assert.Equal(SKColors.Blue, Pixel(pic.CurrentImage));
        }
        finally
        {
            Theme.Current = saved;
            shell.Dispose();
        }
    }

    private static SKColor Pixel(SKImage? image)
    {
        Assert.NotNull(image);
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(0, 0);
    }

    private static void WritePng(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static string DataUrl(SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
    }

    private sealed class Probe : IImageProvider
    {
        public SKColor Color = SKColors.Red;
        public int Calls;

        public SKImage? Load(string id, int requestedWidth, int requestedHeight)
        {
            Calls++;
            using var surface = SKSurface.Create(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null)
                return null;
            surface.Canvas.Clear(Color);
            return surface.Snapshot();
        }
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "winshell-image-cache", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_path);

        public string File(string name) => Path.Combine(_path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
