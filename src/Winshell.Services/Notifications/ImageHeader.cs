using System.Buffers.Binary;
using System.Text;

namespace Winshell.Notifications;

/// <summary>Reads a picture's format and size from the first bytes of its file, without decoding it.</summary>
internal static class ImageHeader
{
    /// <summary>Whether the data starts like a PNG, JPEG, GIF, BMP or WebP. <paramref name="extension"/> is its file extension.</summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out string extension, out long width, out long height)
    {
        extension = data switch
        {
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ".png",
            [0xFF, 0xD8, 0xFF, ..] => ".jpg",
            [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => ".gif",
            [(byte)'B', (byte)'M', ..] => ".bmp",
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ".webp",
            _ => "",
        };
        width = height = 0;
        return extension switch
        {
            ".png" => ReadPng(data, out width, out height),
            ".jpg" => ReadJpeg(data, out width, out height),
            ".gif" => ReadGif(data, out width, out height),
            ".bmp" => ReadBmp(data, out width, out height),
            ".webp" => ReadWebP(data, out width, out height),
            _ => false,
        };
    }

    private static bool ReadPng(ReadOnlySpan<byte> data, out long width, out long height)
    {
        width = height = 0;
        if (data.Length < 24 || !data[12..16].SequenceEqual("IHDR"u8))
            return false;
        width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        return true;
    }

    private static bool ReadGif(ReadOnlySpan<byte> data, out long width, out long height)
    {
        width = height = 0;
        if (data.Length < 10)
            return false;
        width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        return true;
    }

    private static bool ReadBmp(ReadOnlySpan<byte> data, out long width, out long height)
    {
        width = height = 0;
        if (data.Length < 26)
            return false;
        var header = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        if (header == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[20..]);
            return true;
        }
        // A negative height is a picture stored top row first.
        width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data[22..]));
        return header >= 40 && width > 0;
    }

    /// <summary>The size is in the first frame header, after any number of segments.</summary>
    private static bool ReadJpeg(ReadOnlySpan<byte> data, out long width, out long height)
    {
        width = height = 0;
        var at = 2;
        while (at + 4 <= data.Length && data[at] == 0xFF)
        {
            var marker = data[at + 1];
            // 0xFF is padding before a marker, and these have no length after them.
            if (marker is 0xFF or 0x01 or (>= 0xD0 and <= 0xD8))
            {
                at += marker == 0xFF ? 1 : 2;
                continue;
            }
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (at + 9 > data.Length)
                    return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]);
                return true;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            if (length < 2)
                return false;
            at += 2 + length;
        }
        return false;
    }

    private static bool ReadWebP(ReadOnlySpan<byte> data, out long width, out long height)
    {
        width = height = 0;
        if (data.Length < 30)
            return false;
        switch (Encoding.ASCII.GetString(data[12..16]))
        {
            case "VP8 ":
                if (!data[23..26].SequenceEqual<byte>([0x9D, 0x01, 0x2A]))
                    return false;
                width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF;
                return true;
            case "VP8L":
                if (data[20] != 0x2F)
                    return false;
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
                width = (bits & 0x3FFF) + 1;
                height = ((bits >> 14) & 0x3FFF) + 1;
                return true;
            case "VP8X":
                width = (data[24] | (long)data[25] << 8 | (long)data[26] << 16) + 1;
                height = (data[27] | (long)data[28] << 8 | (long)data[29] << 16) + 1;
                return true;
            default:
                return false;
        }
    }
}
