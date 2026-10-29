using System.Runtime.InteropServices;
using SkiaSharp;

namespace Winshell.Platform.Windows.Tests.Snapshots;

/// <summary>
/// Compares a render with its baseline PNG in <c>Snapshots/Baselines</c>. A pixel differs when one of its channels
/// moves by more than <see cref="ChannelTolerance"/>, and a render matches while no more than
/// <see cref="PixelTolerance"/> of its pixels differ. A failure writes the expected, actual and diff images to
/// <c>WINSHELL_SNAPSHOT_DIFFS</c>, or to a folder under the temp folder. With <c>WINSHELL_UPDATE_SNAPSHOTS=1</c>, or
/// set to the name of one surface, a render that does not match replaces its baseline instead;
/// <c>scripts/update-snapshots.ps1</c> sets it.
/// </summary>
internal static class Snapshot
{
    public const int ChannelTolerance = 0;

    public const double PixelTolerance = 0;

    public static string Baselines { get; } = Path.Combine(Repo(), "tests", "Winshell.Platform.Windows.Tests", "Snapshots", "Baselines");

    public static string Diffs { get; } = Environment.GetEnvironmentVariable("WINSHELL_SNAPSHOT_DIFFS") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "winshell-snapshot-diffs");

    private static bool Updating(string name) => Environment.GetEnvironmentVariable("WINSHELL_UPDATE_SNAPSHOTS") is { Length: > 0 } update
        && (update == "1" || name.StartsWith(update + "-", StringComparison.Ordinal));

    /// <summary>Fails unless <paramref name="actual"/>, an RGBA render, matches the baseline called <paramref name="name"/>.</summary>
    public static void Match(string name, SKBitmap actual)
    {
        var baseline = Path.Combine(Baselines, name + ".png");
        using var expected = File.Exists(baseline) ? Decode(baseline) : null;
        var differing = expected is null ? -1 : Differing(expected, actual);
        var allowed = (int)(actual.Width * actual.Height * PixelTolerance);
        if (differing >= 0 && differing <= allowed)
            return;
        if (Updating(name))
        {
            Directory.CreateDirectory(Baselines);
            Save(actual, baseline);
            return;
        }
        Directory.CreateDirectory(Diffs);
        Save(actual, Path.Combine(Diffs, name + ".actual.png"));
        if (expected is null)
            Assert.Fail($"{name} has no baseline. Its render is in {Diffs}. Run scripts/update-snapshots.ps1 to add it.");
        Save(expected, Path.Combine(Diffs, name + ".expected.png"));
        if (differing == int.MaxValue)
            Assert.Fail($"{name} is {actual.Width}×{actual.Height}; its baseline is {expected.Width}×{expected.Height}. The images are in {Diffs}.");
        using (var diff = Diff(expected, actual))
            Save(diff, Path.Combine(Diffs, name + ".diff.png"));
        Assert.Fail($"{name}: {differing} pixels differ, more than the {allowed} allowed. The images are in {Diffs}. If the change is intended, run scripts/update-snapshots.ps1.");
    }

    /// <summary>How many pixels differ by more than <see cref="ChannelTolerance"/>, or <see cref="int.MaxValue"/> when the sizes do.</summary>
    public static int Differing(SKBitmap expected, SKBitmap actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return int.MaxValue;
        var a = expected.GetPixelSpan();
        var b = actual.GetPixelSpan();
        var differing = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (Differs(a, b, i))
                differing++;
        }
        return differing;
    }

    /// <summary>The expected render, faded to gray, with the pixels that differ in magenta.</summary>
    public static SKBitmap Diff(SKBitmap expected, SKBitmap actual)
    {
        var a = expected.GetPixelSpan();
        var b = actual.GetPixelSpan();
        var pixels = new byte[a.Length];
        for (var i = 0; i < a.Length; i += 4)
        {
            if (Differs(a, b, i))
            {
                pixels[i] = 255;
                pixels[i + 1] = 0;
                pixels[i + 2] = 255;
            }
            else
            {
                var gray = (byte)(192 + (a[i] * 299 + a[i + 1] * 587 + a[i + 2] * 114) / 1000 / 4);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
            }
            pixels[i + 3] = 255;
        }
        var diff = new SKBitmap(new SKImageInfo(expected.Width, expected.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        Marshal.Copy(pixels, 0, diff.GetPixels(), pixels.Length);
        return diff;
    }

    private static bool Differs(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int i) =>
        Math.Abs(a[i] - b[i]) > ChannelTolerance || Math.Abs(a[i + 1] - b[i + 1]) > ChannelTolerance
        || Math.Abs(a[i + 2] - b[i + 2]) > ChannelTolerance || Math.Abs(a[i + 3] - b[i + 3]) > ChannelTolerance;

    /// <summary>A PNG as an RGBA bitmap, as renders are.</summary>
    public static SKBitmap Decode(string path)
    {
        using var decoded = SKBitmap.Decode(path);
        return decoded.Copy(SKColorType.Rgba8888);
    }

    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("winshell.slnx was not found above the test directory.");
    }
}
