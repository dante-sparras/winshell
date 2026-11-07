using System.Buffers.Binary;
using System.Text;

namespace Winshell.Services.Tests;

public sealed class ImageHeaderTests
{
    // 3 by 2 pictures from real encoders: Skia for the PNG, the JPEG and the lossy WebP.
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAYAAACddGYaAAAABHNCSVQICAgIfAhkiAAAAB5JREFUCJkFwcEJAAAIBCAvgl7tP25pcCNGtA0O5QEeFQIkjAn5PAAAAABJRU5ErkJggg==";

    private const string Jpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAACAAMDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAZEAADAAMAAAAAAAAAAAAAAAAAAQISIpH/xAAVAQEBAAAAAAAAAAAAAAAAAAACA//EABYRAQEBAAAAAAAAAAAAAAAAAAABMf/aAAwDAQACEQMRAD8AmKZnFargAApMf//Z";

    private const string WebP = "UklGRjAAAABXRUJQVlA4ICQAAADQAQCdASoDAAIAAUAmJZwCdAEfbfUlgAD+/wuX85lhocmFMAA=";

    private static byte[] Bytes(string base64)
    {
        return Convert.FromBase64String(base64);
    }

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
    private static byte[] LosslessWebP()
    {
        return Riff("VP8L", 0x2F, 0x27, 0x63, 0x00, 0x00);
    }

    // A 3-byte width 20000 less one, 0x4E1F, and a 3-byte height 3 less one.
    private static byte[] ExtendedWebP()
    {
        return Riff("VP8X", 0, 0, 0, 0, 0x1F, 0x4E, 0, 2, 0, 0);
    }

    private static byte[] Segment(byte marker, params byte[] body)
    {
        return [0xFF, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2), .. body];
    }

    private static byte[] JpegFrame(byte marker, ushort width, ushort height)
    {
        return Segment(marker, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3);
    }

    [Fact]
    public void Pictures_from_real_encoders_give_their_size()
    {
        Assert.Equal(((uint)3, (uint)2), Size(Bytes(Png)));
        Assert.Equal(((uint)3, (uint)2), Size(Bytes(Jpeg)));
        Assert.Equal(((uint)3, (uint)2), Size(Bytes(WebP)));
    }

    [Fact]
    public void A_picture_reports_the_extension_its_header_names()
    {
        Assert.Equal(".png", Extension(Bytes(Png)));
        Assert.Equal(".jpg", Extension(Bytes(Jpeg)));
        Assert.Equal(".gif", Extension(GifOf(3, 2)));
        Assert.Equal(".bmp", Extension(BmpOf(3, 2)));
        Assert.Equal(".bmp", Extension(CoreBmpOf(3, 2)));
        Assert.Equal(".webp", Extension(Bytes(WebP)));
        Assert.Equal(".webp", Extension(LosslessWebP()));
        Assert.Equal(".webp", Extension(ExtendedWebP()));
    }

    private static string Extension(byte[] data)
    {
        var header = ImageHeader.Read(data);
        Assert.NotNull(header);
        Assert.True(header.Value.Width > 0);
        Assert.True(header.Value.Height > 0);
        return header.Value.Extension;
    }

    private static (uint, uint)? Size(ReadOnlySpan<byte> data)
    {
        return ImageHeader.Read(data) is var (_, width, height) ? (width, height) : null;
    }

    [Fact]
    public void Every_format_reads_its_own_size_fields()
    {
        Assert.Equal(((uint)8193, (uint)2049), Size(PngOf(8193, 2049)));
        Assert.Equal(((uint)640, (uint)480), Size(GifOf(640, 480)));
        Assert.Equal(((uint)640, (uint)480), Size(BmpOf(640, 480)));
        Assert.Equal(((uint)640, (uint)480), Size(CoreBmpOf(640, 480)));
        Assert.Equal(((uint)9000, (uint)2), Size(LosslessWebP()));
        Assert.Equal(((uint)20000, (uint)3), Size(ExtendedWebP()));
    }

    [Fact]
    public void A_top_down_bitmap_has_a_negative_height()
    {
        Assert.Equal(((uint)640, (uint)480), Size(BmpOf(640, -480)));
        Assert.Equal(((uint)1, (uint)int.MaxValue + 1), Size(BmpOf(1, int.MinValue)));
    }

    [Fact]
    public void A_jpeg_size_comes_from_its_frame_and_not_a_thumbnail_before_it()
    {
        byte[] thumbnail = [.. JpegFrame(0xC0, 160, 120)];
        byte[] jpeg =
            [0xFF, 0xD8, .. Segment(0xE1, thumbnail), .. Segment(0xDB, 1, 2, 3), .. JpegFrame(0xC2, 8000, 8000)];

        Assert.Equal(((uint)8000, (uint)8000), Size(jpeg));
    }

    [Theory]
    [InlineData(0xC4)] // DHT
    [InlineData(0xC8)] // JPG
    [InlineData(0xCC)] // DAC
    public void A_marker_inside_the_frame_range_is_not_a_frame(byte marker)
    {
        byte[] jpeg = [0xFF, 0xD8, .. Segment(marker, 8, 1, 1, 1, 1, 3), .. JpegFrame(0xC0, 32, 16)];

        Assert.Equal(((uint)32, (uint)16), Size(jpeg));
    }

    [Fact]
    public void A_jpeg_with_no_frame_before_its_scan_has_no_size()
    {
        byte[] jpeg = [0xFF, 0xD8, .. Segment(0xDA, 1, 2, 3), .. JpegFrame(0xC0, 32, 16)];

        Assert.Null(Size(jpeg));
    }

    [Fact]
    public void A_segment_that_claims_no_length_still_ends()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0xFF, 0xE0, 0, 0];

        Assert.Null(Size(jpeg));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100000\" height=\"100000\"/>")]
    [InlineData("not a picture")]
    [InlineData("")]
    public void Anything_else_has_no_size(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        Assert.Null(Size(data));
    }

    [Fact]
    public void A_webp_with_a_broken_frame_has_no_size()
    {
        Assert.Null(Size(Riff("VP8 ", 0, 0, 0, 1, 2, 3)));
        Assert.Null(Size(Riff("VP8L", 0x2E, 0, 0, 0, 0)));
        Assert.Null(Size(Riff("ABCD", 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void A_cut_off_header_has_no_size_and_does_not_throw()
    {
        foreach (var data in new[]
                 {
                     Bytes(Png), Bytes(Jpeg), GifOf(3, 2), BmpOf(3, 2), CoreBmpOf(3, 2), Bytes(WebP), LosslessWebP(),
                     ExtendedWebP()
                 })
        {
            var whole = Size(data);
            Assert.NotNull(whole);
            for (var length = 0; length < data.Length; length++)
            {
                var size = Size(data.AsSpan(0, length));
                Assert.True(size is null || size == whole, $"{length} bytes gave {size}");
            }
        }
    }
}
