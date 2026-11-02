using System.Buffers.Binary;

namespace Winshell.Media;

/// <summary>Reads what kind of picture a file says it is, and how big, without decoding it.</summary>
internal static class ImageHeader
{
    /// <summary>The size in a PNG, JPEG, GIF, BMP or WebP header, or null for anything else.</summary>
    public static (uint Width, uint Height)? ReadSize(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith<byte>([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return Png(data);
        if (data.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
            return Jpeg(data);
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
            return Gif(data);
        if (data.StartsWith("BM"u8))
            return Bmp(data);
        if (data.StartsWith("RIFF"u8) && data.Length >= 12 && data[8..12].SequenceEqual("WEBP"u8))
            return WebP(data);
        return null;
    }

    private static (uint, uint)? Png(ReadOnlySpan<byte> data) =>
        data.Length >= 24 && data[12..16].SequenceEqual("IHDR"u8)
            ? (BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..]))
            : null;

    /// <summary>Walks the segments to the first frame header, the one a decoder uses. An embedded thumbnail sits inside a segment.</summary>
    private static (uint, uint)? Jpeg(ReadOnlySpan<byte> data)
    {
        const byte fill = 0xFF;
        const byte startOfScan = 0xDA;
        const byte endOfImage = 0xD9;
        var at = 2;
        while (at + 1 < data.Length)
        {
            // Like a decoder, skip anything that is not a marker.
            if (data[at] != fill || data[at + 1] is fill or 0)
            {
                at++;
                continue;
            }
            var marker = data[at + 1];
            at += 2;
            if (marker is startOfScan or endOfImage)
                return null;
            // The restart markers and TEM have no length.
            if (marker is >= 0xD0 and <= 0xD8 or 0x01)
                continue;
            if (at + 2 > data.Length)
                return null;
            // SOF0 to SOF15, leaving out DHT, JPG and DAC, which share that range.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                return at + 7 <= data.Length
                    ? (BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(at + 3)..]))
                    : null;
            }
            at += BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
        }
        return null;
    }

    private static (uint, uint)? Gif(ReadOnlySpan<byte> data) =>
        data.Length >= 10
            ? (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]))
            : null;

    private static (uint, uint)? Bmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
            return null;
        var header = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        // The old OS/2 header holds 16-bit sizes. Later ones hold 32-bit sizes, and a top-down picture has a negative height.
        if (header == 12)
        {
            return data.Length >= 22
                ? (BinaryPrimitives.ReadUInt16LittleEndian(data[18..]), BinaryPrimitives.ReadUInt16LittleEndian(data[20..]))
                : null;
        }
        if (header < 16 || data.Length < 26)
            return null;
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var height = (long)BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        return width > 0 ? ((uint)width, (uint)Math.Abs(height)) : null;
    }

    private static (uint, uint)? WebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
            return null;
        var chunk = data[12..16];
        var payload = data[20..];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // A 3-byte frame tag, the start code, then two 14-bit sizes that each share a word with a scale.
            return payload[3..6].SequenceEqual<byte>([0x9D, 0x01, 0x2A])
                ? ((uint)(BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF), (uint)(BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF))
                : null;
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            // A signature byte, then 14-bit sizes less one.
            var sizes = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
            return payload[0] == 0x2F ? ((sizes & 0x3FFF) + 1, ((sizes >> 14) & 0x3FFF) + 1) : null;
        }
        // Flags, three reserved bytes, then 24-bit canvas sizes less one.
        return chunk.SequenceEqual("VP8X"u8) ? (UInt24(payload[4..]) + 1, UInt24(payload[7..]) + 1) : null;
    }

    private static uint UInt24(ReadOnlySpan<byte> data) => (uint)(data[0] | (data[1] << 8) | (data[2] << 16));
}
