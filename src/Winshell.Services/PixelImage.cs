using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Winshell;

/// <summary>
/// A picture in memory: BGRA, one byte a channel, top row first, with no gap between rows. Nothing writes to the
/// pixels after this is made, so the constructor keeps <c>bgra</c> rather than copying it, and a UI can hold on to one.
/// </summary>
public sealed class PixelImage
{
    private readonly byte[] _bgra;

    /// <param name="premultiplied">Whether the color channels are already multiplied by alpha.</param>
    public PixelImage(int width, int height, byte[] bgra, bool premultiplied = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.Length != checked(width * height * 4))
            throw new ArgumentException("The pixels must be width times height times 4 bytes.", nameof(bgra));
        Width = width;
        Height = height;
        Premultiplied = premultiplied;
        _bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    public bool Premultiplied { get; }

    public ReadOnlySpan<byte> Bgra => _bgra;

    /// <summary>The picture as a PNG file. Blocks while Windows encodes it.</summary>
    public byte[] EncodePng()
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask().GetAwaiter().GetResult();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)Width, (uint)Height, 96, 96, StraightBgra());
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
        using var encoded = new MemoryStream();
        using (var input = output.GetInputStreamAt(0).AsStreamForRead())
            input.CopyTo(encoded);
        return encoded.ToArray();
    }

    /// <summary>The pixels with straight alpha, as a PNG and the clipboard keep them. Read-only: it can be the image's own array.</summary>
    internal byte[] StraightBgra()
    {
        if (!Premultiplied)
            return _bgra;
        var straight = (byte[])_bgra.Clone();
        for (var i = 0; i < straight.Length; i += 4)
        {
            var alpha = straight[i + 3];
            if (alpha is 0 or 255)
                continue;
            for (var channel = i; channel < i + 3; channel++)
                straight[channel] = (byte)Math.Min(255, (straight[channel] * 255 + alpha / 2) / alpha);
        }
        return straight;
    }
}
