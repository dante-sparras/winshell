using System.Net.Http;
using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Canvas;

namespace Winshell.Runtime.Tests;

/// <summary>Members from the Qt differences table that now follow Qt 6.10.</summary>
public class QtPaintGapTests
{
    [Fact]
    public void ToDataURL_and_save_use_the_named_format()
    {
        var (root, shell) = Show("""
            import QtQuick
            Item {
              Canvas {
                objectName: "canvas"
                width: 4; height: 4
                onPaint: { const c = getContext("2d"); c.fillStyle = "red"; c.fillRect(0, 0, 4, 4) }
              }
            }
            """);
        using var _ = shell;
        var canvas = root.ChildItems.OfType<Canvas>().Single();

        Assert.StartsWith("data:image/png;base64,", canvas.ToDataURL());
        var jpeg = canvas.ToDataURL("image/jpeg");
        Assert.StartsWith("data:image/jpeg;base64,", jpeg);
        var bytes = Convert.FromBase64String(jpeg["data:image/jpeg;base64,".Length..]);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, bytes[..3]);
        Assert.Equal("data:,", canvas.ToDataURL("image/tiff"));

        Assert.True(canvas.Save("shot.jpg"));
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, File.ReadAllBytes(Path.Combine(shell.Directory, "shot.jpg"))[..3]);
        Assert.False(canvas.Save("shot.tiff"));
        Assert.False(canvas.Save("shot.bmp"));
    }

    [Fact]
    public void A_beveled_rectangle_cuts_its_corners()
    {
        var (root, shell) = Show("""
            import QtQuick
            import QtQuick.Shapes
            Item {
              Shape {
                objectName: "shape"
                width: 40; height: 40
                antialiasing: false
                ShapePath {
                  fillColor: "red"; strokeWidth: 0
                  PathRectangle { width: 40; height: 40; radius: 16; bevel: true }
                }
              }
            }
            """);
        using var _ = shell;
        using var bitmap = Paint((Item)root.ChildItems.Single(i => i.ObjectName == "shape"), 40, 40);

        // (8, 4) is inside a rounded corner of radius 16 and outside the bevel cut.
        Assert.Equal(0, bitmap.GetPixel(8, 4).Alpha);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(20, 20));
    }

    [Fact]
    public void Blur_multiplier_extends_the_blur_and_zero_does_not()
    {
        byte Halo(string multiplier)
        {
            var (root, shell) = Show($$"""
                import QtQuick
                import QtQuick.Effects
                Item {
                  Item {
                    id: source
                    width: 40; height: 40
                    Rectangle { x: 16; width: 24; height: 40; color: "black" }
                  }
                  MultiEffect {
                    objectName: "effect"
                    width: 40; height: 40
                    source: source
                    blurEnabled: true
                    blur: 1
                    blurMax: 8
                    blurMultiplier: {{multiplier}}
                  }
                }
                """);
            using var _ = shell;
            using var bitmap = Paint((Item)root.ChildItems.Single(i => i.ObjectName == "effect"), 40, 40);
            return bitmap.GetPixel(4, 20).Alpha;
        }

        var plain = Halo("0");
        var wide = Halo("0.5");
        var wider = Halo("4");
        Assert.True(wide > plain, $"multiplier 0.5 alpha {wide} was not wider than {plain}");
        Assert.True(wider > wide + 20, $"multiplier 4 alpha {wider} was not wider than {wide}");
    }

    [Fact]
    public void An_asynchronous_image_is_not_cached_when_cache_is_false()
    {
        ImageLoader.ClearCache();
        using var shell = new TestShell();
        WritePng(Path.Combine(shell.Directory, "pic.png"));
        shell.Write("shell.qml", """
            import QtQuick
            Image { asynchronous: true; cache: false; source: "pic.png" }
            """);
        var image = (Image)shell.Load();
        Wait(shell, image);

        Assert.Equal((int)Image.StatusValue.Ready, image.Status);
        Assert.Equal(0, ImageLoader.CachedCount);
    }

    [Fact]
    public void A_remote_image_is_not_cached_when_cache_is_false()
    {
        ImageLoader.ClearCache();
        var previous = ImageLoader.Http;
        ImageLoader.Http = new HttpClient(new PngHandler());
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            Image { cache: false; source: "https://example.test/one.png" }
            """));
        try
        {
            var image = (Image)shell.Load();
            Wait(shell, image);

            Assert.Equal((int)Image.StatusValue.Ready, image.Status);
            Assert.Equal(0, ImageLoader.CachedCount);
        }
        finally
        {
            ImageLoader.Http = previous;
        }
    }

    private static (Item Root, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        shell.Loop.Process();
        return (root, shell);
    }

    private static SKBitmap Paint(Item item, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        item.Paint(canvas, new PaintContext());
        return bitmap;
    }

    private static void Wait(TestShell shell, Image image)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (image.Status == (int)Image.StatusValue.Loading && DateTime.UtcNow < until)
        {
            Thread.Sleep(10);
            shell.Loop.Process();
        }
    }

    private static void WritePng(string path)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2));
        bitmap.Erase(SKColors.Red);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private sealed class PngHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(2, 2));
            bitmap.Erase(SKColors.Blue);
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent(data.ToArray()) });
        }
    }
}
