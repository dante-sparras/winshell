using System.Security.AccessControl;
using System.Security.Principal;
using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ImageDecodeLimitTests
{
    private static readonly SKColor Grey = new(0x80, 0x80, 0x80);

    private static void WritePng(string path, int width, int height)
    {
        // One grey channel and the fastest compression keep a 100 MP picture quick to make.
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        bitmap.Erase(Grey);
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.NoFilters, 1));
        File.WriteAllBytes(path, data!.ToArray());
    }

    private static void WriteJpeg(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(Grey);
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static Image Show(TestShell shell, string imageBody)
    {
        shell.Write("shell.qml", $$"""
            import QtQuick
            Image { cache: false; {{imageBody}} }
            """);
        return (Image)shell.Load();
    }

    [Fact]
    public void A_huge_png_decodes_to_at_most_64_megapixels()
    {
        using var shell = new TestShell();
        WritePng(Path.Combine(shell.Directory, "huge.png"), 10000, 10000);

        var image = Show(shell, """source: "huge.png" """);

        Assert.Equal((int)Image.StatusValue.Ready, image.Status);
        var decoded = image.CurrentImage!;
        Assert.True((long)decoded.Width * decoded.Height <= ImageLoader.MaxPixels, $"{decoded.Width}x{decoded.Height}");
        Assert.Equal(decoded.Width, decoded.Height);
        Assert.Equal(Grey, Pixel(decoded, decoded.Width / 2, decoded.Height / 2));
    }

    [Fact]
    public void A_source_size_with_only_a_width_keeps_the_aspect_ratio()
    {
        using var shell = new TestShell();
        // A JPEG codec decodes at a quarter size, so the picture is decoded smaller, not scaled after.
        WriteJpeg(Path.Combine(shell.Directory, "wide.jpg"), 400, 200);

        var image = Show(shell, """source: "wide.jpg"; sourceSize: Qt.size(100, 0) """);

        Assert.Equal((int)Image.StatusValue.Ready, image.Status);
        Assert.Equal(100, image.CurrentImage!.Width);
        Assert.Equal(50, image.CurrentImage.Height);
        Assert.Equal(100, image.ImplicitWidth);
        Assert.Equal(50, image.ImplicitHeight);
    }

    [Fact]
    public void A_source_size_with_only_a_height_keeps_the_aspect_ratio()
    {
        using var shell = new TestShell();
        WriteJpeg(Path.Combine(shell.Directory, "wide.jpg"), 400, 200);

        var image = Show(shell, """source: "wide.jpg"; sourceSize: Qt.size(0, 50) """);

        Assert.Equal(100, image.CurrentImage!.Width);
        Assert.Equal(50, image.CurrentImage.Height);
    }

    [Fact]
    public void Bad_base64_in_a_data_url_is_an_error_and_does_not_throw()
    {
        using var shell = new TestShell();

        var image = Show(shell, """source: "data:image/png;base64,!!not base64!!" """);

        Assert.Equal((int)Image.StatusValue.Error, image.Status);
        Assert.Null(image.CurrentImage);
        Assert.Contains(shell.Messages, m => m.Contains("Cannot open", StringComparison.Ordinal));
    }

    [Fact]
    public void Setting_a_bad_data_url_from_script_does_not_throw()
    {
        using var shell = new TestShell();
        var image = Show(shell, "");

        shell.Eval(image, """source = "data:image/png;base64,%%%" """);

        Assert.Equal((int)Image.StatusValue.Error, image.Status);
        Assert.DoesNotContain(shell.Messages, m => m.Contains("FormatException", StringComparison.Ordinal));
    }

    [Fact]
    public void An_svg_with_an_absurd_size_is_capped_or_an_error_and_does_not_throw()
    {
        using var shell = new TestShell();
        shell.Write("absurd.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="1000000000" height="1000000000">
              <rect width="1000000000" height="1000000000" fill="red"/>
            </svg>
            """);

        var image = Show(shell, """source: "absurd.svg" """);

        if (image.CurrentImage is { } decoded)
        {
            Assert.Equal((int)Image.StatusValue.Ready, image.Status);
            Assert.True((long)decoded.Width * decoded.Height <= ImageLoader.MaxPixels, $"{decoded.Width}x{decoded.Height}");
        }
        else
        {
            Assert.Equal((int)Image.StatusValue.Error, image.Status);
        }
    }

    [Fact]
    public void An_unreadable_file_is_an_error()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var shell = new TestShell();
        var path = Path.Combine(shell.Directory, "locked.png");
        WritePng(path, 4, 4);
        var file = new FileInfo(path);
        var user = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(user, FileSystemRights.ReadData, AccessControlType.Deny);
        var security = file.GetAccessControl();
        security.AddAccessRule(deny);
        file.SetAccessControl(security);
        try
        {
            var image = Show(shell, """source: "locked.png" """);

            Assert.Equal((int)Image.StatusValue.Error, image.Status);
            Assert.Null(image.CurrentImage);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            file.SetAccessControl(security);
        }
    }

    private static SKColor Pixel(SKImage image, int x, int y)
    {
        using var pixmap = image.PeekPixels();
        return pixmap.GetPixelColor(x, y);
    }
}
