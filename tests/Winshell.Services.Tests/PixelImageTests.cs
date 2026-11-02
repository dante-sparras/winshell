using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Winshell.Services.Tests;

public sealed class PixelImageTests
{
    private static async Task<byte[]> DecodeAsync(byte[] png)
    {
        using var stream = new MemoryStream(png).AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, stream);
        Assert.Equal(2u, decoder.PixelWidth);
        Assert.Equal(1u, decoder.PixelHeight);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return pixels.DetachPixelData();
    }

    [Fact]
    public async Task A_png_keeps_the_size_and_the_pixels()
    {
        byte[] pixels = [10, 20, 30, 255, 40, 50, 60, 128];

        var png = new PixelImage(2, 1, pixels).EncodePng();

        Assert.Equal(pixels, await DecodeAsync(png));
    }

    [Fact]
    public async Task A_premultiplied_picture_is_saved_with_straight_alpha()
    {
        var png = new PixelImage(2, 1, [64, 32, 16, 128, 0, 0, 0, 0], premultiplied: true).EncodePng();

        Assert.Equal(new byte[] { 128, 64, 32, 128, 0, 0, 0, 0 }, await DecodeAsync(png));
    }

    [Fact]
    public async Task A_png_can_be_made_on_a_UI_thread()
    {
        byte[] pixels = [10, 20, 30, 255, 40, 50, 60, 128];
        byte[]? png = null;
        var thread = new Thread(() => png = new PixelImage(2, 1, pixels).EncodePng());
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        Assert.Equal(pixels, await DecodeAsync(png!));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(-1, -1, 4)]
    public void A_picture_has_a_size(int width, int height, int bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelImage(width, height, new byte[bytes]));

    [Fact]
    public void The_pixels_must_fill_the_picture() =>
        Assert.Throws<ArgumentException>(() => new PixelImage(2, 2, new byte[15]));
}
