using System.Buffers.Binary;
using System.Text;
using Winshell.Media;

namespace Winshell.Services.Tests;

public sealed class ImageHeaderTests
{
    // 3 by 2 pictures from real encoders: Skia for the PNG, the JPEG and the lossy WebP.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAYAAACddGYaAAAABHNCSVQICAgIfAhkiAAAAB5JREFUCJkFwcEJAAAIBCAvgl7tP25pcCNGtA0O5QEeFQIkjAn5PAAAAABJRU5ErkJggg==";
    private const string Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAACAAMDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAZEAADAAMAAAAAAAAAAAAAAAAAAQISIpH/xAAVAQEBAAAAAAAAAAAAAAAAAAACA//EABYRAQEBAAAAAAAAAAAAAAAAAAABMf/aAAwDAQACEQMRAD8AmKZnFargAApMf//Z";
    private const string WebP = "UklGRjAAAABXRUJQVlA4ICQAAADQAQCdASoDAAIAAUAmJZwCdAEfbfUlgAD+/wuX85lhocmFMAA=";

    private static byte[] Bytes(string base64) => Convert.FromBase64String(base64);

    private static byte[] PngOf(uint width, uint height)
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(data, 0);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    private static byte[] GifOf(ushort width, ushort height)
    {
        var data = new byte[13];
        "GIF89a"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), height);
        return data;
    }

    private static byte[] BmpOf(int width, int height)
    {
        var data = new byte[54];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        return data;
    }

    private static byte[] CoreBmpOf(ushort width, ushort height)
    {
        var data = new byte[26];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), height);
        return data;
    }

    private static byte[] Riff(string chunk, params byte[] payload)
    {
        var data = new byte[Math.Max(30, 20 + payload.Length)];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        Encoding.ASCII.GetBytes(chunk).CopyTo(data, 12);
        payload.CopyTo(data, 20);
        return data;
    }

    // Width 9000 less one is 0x2327, and height 2 less one is 1, packed into 14 bits each.
    private static byte[] LosslessWebP() => Riff("VP8L", 0x2F, 0x27, 0x63, 0x00, 0x00);

    // A 3-byte width 20000 less one, 0x4E1F, and a 3-byte height 3 less one.
    private static byte[] ExtendedWebP() => Riff("VP8X", 0, 0, 0, 0, 0x1F, 0x4E, 0, 2, 0, 0);

    private static byte[] Segment(byte marker, params byte[] body) =>
        [0xFF, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2), .. body];

    private static byte[] JpegFrame(byte marker, ushort width, ushort height) =>
        Segment(marker, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3);

    [Fact]
    public void Pictures_from_real_encoders_give_their_size()
    {
        Assert.Equal(((uint)3, (uint)2), ImageHeader.ReadSize(Bytes(Png)));
        Assert.Equal(((uint)3, (uint)2), ImageHeader.ReadSize(Bytes(Jpeg)));
        Assert.Equal(((uint)3, (uint)2), ImageHeader.ReadSize(Bytes(WebP)));
    }

    [Fact]
    public void Every_format_reads_its_own_size_fields()
    {
        Assert.Equal(((uint)8193, (uint)2049), ImageHeader.ReadSize(PngOf(8193, 2049)));
        Assert.Equal(((uint)640, (uint)480), ImageHeader.ReadSize(GifOf(640, 480)));
        Assert.Equal(((uint)640, (uint)480), ImageHeader.ReadSize(BmpOf(640, 480)));
        Assert.Equal(((uint)640, (uint)480), ImageHeader.ReadSize(CoreBmpOf(640, 480)));
        Assert.Equal(((uint)9000, (uint)2), ImageHeader.ReadSize(LosslessWebP()));
        Assert.Equal(((uint)20000, (uint)3), ImageHeader.ReadSize(ExtendedWebP()));
    }

    [Fact]
    public void A_top_down_bitmap_has_a_negative_height()
    {
        Assert.Equal(((uint)640, (uint)480), ImageHeader.ReadSize(BmpOf(640, -480)));
        Assert.Equal(((uint)1, (uint)int.MaxValue + 1), ImageHeader.ReadSize(BmpOf(1, int.MinValue)));
    }

    [Fact]
    public void A_jpeg_size_comes_from_its_frame_and_not_a_thumbnail_before_it()
    {
        byte[] thumbnail = [.. JpegFrame(0xC0, 160, 120)];
        byte[] jpeg = [0xFF, 0xD8, .. Segment(0xE1, thumbnail), .. Segment(0xDB, 1, 2, 3), .. JpegFrame(0xC2, 8000, 8000)];

        Assert.Equal(((uint)8000, (uint)8000), ImageHeader.ReadSize(jpeg));
    }

    [Theory]
    [InlineData(0xC4)] // DHT
    [InlineData(0xC8)] // JPG
    [InlineData(0xCC)] // DAC
    public void A_marker_inside_the_frame_range_is_not_a_frame(byte marker)
    {
        byte[] jpeg = [0xFF, 0xD8, .. Segment(marker, 8, 1, 1, 1, 1, 3), .. JpegFrame(0xC0, 32, 16)];

        Assert.Equal(((uint)32, (uint)16), ImageHeader.ReadSize(jpeg));
    }

    [Fact]
    public void A_jpeg_with_no_frame_before_its_scan_has_no_size()
    {
        byte[] jpeg = [0xFF, 0xD8, .. Segment(0xDA, 1, 2, 3), .. JpegFrame(0xC0, 32, 16)];

        Assert.Null(ImageHeader.ReadSize(jpeg));
    }

    [Fact]
    public void A_segment_that_claims_no_length_still_ends()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0xFF, 0xE0, 0, 0];

        Assert.Null(ImageHeader.ReadSize(jpeg));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100000\" height=\"100000\"/>")]
    [InlineData("not a picture")]
    [InlineData("")]
    public void Anything_else_has_no_size(string text) => Assert.Null(ImageHeader.ReadSize(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_webp_with_a_broken_frame_has_no_size()
    {
        Assert.Null(ImageHeader.ReadSize(Riff("VP8 ", 0, 0, 0, 1, 2, 3)));
        Assert.Null(ImageHeader.ReadSize(Riff("VP8L", 0x2E, 0, 0, 0, 0)));
        Assert.Null(ImageHeader.ReadSize(Riff("ABCD", 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void A_cut_off_header_has_no_size_and_does_not_throw()
    {
        foreach (var data in new[] { Bytes(Png), Bytes(Jpeg), GifOf(3, 2), BmpOf(3, 2), CoreBmpOf(3, 2), Bytes(WebP), LosslessWebP(), ExtendedWebP() })
        {
            var whole = ImageHeader.ReadSize(data);
            Assert.NotNull(whole);
            for (var length = 0; length < data.Length; length++)
            {
                var size = ImageHeader.ReadSize(data.AsSpan(0, length));
                Assert.True(size is null || size == whole, $"{length} bytes gave {size}");
            }
        }
    }

    [Fact]
    public async Task Art_within_the_limits_is_stored()
    {
        foreach (var art in new[] { Bytes(Png), Bytes(Jpeg), Bytes(WebP), PngOf(4096, 4096), GifOf(3, 2), BmpOf(3, 2) })
        {
            var url = await ArtCache.StoreAsync(new MemoryStream(art), "image/png");

            Assert.False(string.IsNullOrEmpty(url));
            Assert.True(File.Exists(new Uri(url).LocalPath));
            ArtCache.Release(url);
            Assert.False(File.Exists(new Uri(url).LocalPath));
        }
    }

    [Fact]
    public async Task Art_that_says_it_has_too_many_pixels_is_dropped()
    {
        foreach (var art in new[] { PngOf(8193, 2049), PngOf(uint.MaxValue, uint.MaxValue), PngOf(4097, 4096), GifOf(65535, 65535), BmpOf(9000, -9000), Riff("VP8X", 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF) })
            Assert.Null(await ArtCache.StoreAsync(new MemoryStream(art), "image/png"));
    }

    [Fact]
    public async Task Art_with_no_pixels_is_dropped()
    {
        foreach (var art in new[] { PngOf(0, 2), GifOf(3, 0), BmpOf(0, 2), BmpOf(3, 0), BmpOf(-3, 2) })
            Assert.Null(await ArtCache.StoreAsync(new MemoryStream(art), "image/png"));
    }

    [Fact]
    public async Task No_art_is_no_url()
    {
        Assert.Equal("", await ArtCache.StoreAsync(new MemoryStream(), "image/png"));
    }
}
