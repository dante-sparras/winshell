using System.Globalization;
using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Platform.Windows.Tests.Snapshots;

/// <summary>
/// Each surface of the default shell, rendered offscreen at 100%, 150% and 200% in light and dark, and compared with
/// its baseline. See <see cref="Snapshot"/> and docs/development.md.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class SnapshotTests
{
    public static readonly string[] Surfaces = ["taskbar", "start", "quick-settings", "notification-center", "osd", "settings"];

    public static TheoryData<string, string, double> Cases()
    {
        var cases = new TheoryData<string, string, double>();
        foreach (var surface in Surfaces)
            foreach (var theme in new[] { "light", "dark" })
                foreach (var scale in new[] { 1, 1.5, 2 })
                    cases.Add(surface, theme, scale);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Surface_looks_as_it_did(string surface, string theme, double scale)
    {
        using var shell = new SnapshotShell(theme == "dark", scale);
        var window = shell.Open(surface);
        using var render = shell.Backend(window).Render(Theme.Current.SolidBackgroundFillColorBase.ToSk());
        Snapshot.Match($"{surface}-{theme}-{(scale * 100).ToString(CultureInfo.InvariantCulture)}", render);
        Assert.DoesNotContain(shell.Messages, m => !m.StartsWith("hotkey:", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_accent_does_not_pass_for_the_baseline()
    {
        using var shell = new SnapshotShell(dark: false, scale: 1);
        Theme.Current = Theme.Current with { Accents = AccentColors.From(QColor.Parse("#e81123")) };
        var window = shell.Open("quick-settings");
        using var render = shell.Backend(window).Render(Theme.Current.SolidBackgroundFillColorBase.ToSk());
        using var baseline = Snapshot.Decode(Path.Combine(Snapshot.Baselines, "quick-settings-light-100.png"));
        Assert.True(Snapshot.Differing(baseline, render) > render.Width * render.Height * Snapshot.PixelTolerance);
    }

    [Fact]
    public void Small_shifts_in_a_pixel_are_tolerated_and_larger_ones_counted()
    {
        using var expected = Solid(4, 2, new SKColor(100, 100, 100));
        using var actual = Solid(4, 2, new SKColor(100, 100, 100));
        actual.SetPixel(0, 0, new SKColor(100 + Snapshot.ChannelTolerance, 100, 100));
        Assert.Equal(0, Snapshot.Differing(expected, actual));
        actual.SetPixel(1, 0, new SKColor(100, 100, 101 + Snapshot.ChannelTolerance));
        actual.SetPixel(3, 1, SKColors.Black);
        Assert.Equal(2, Snapshot.Differing(expected, actual));
        using var diff = Snapshot.Diff(expected, actual);
        Assert.Equal(SKColors.Magenta, diff.GetPixel(1, 0));
        Assert.Equal(SKColors.Magenta, diff.GetPixel(3, 1));
        Assert.NotEqual(SKColors.Magenta, diff.GetPixel(0, 0));
        using var smaller = Solid(4, 1, new SKColor(100, 100, 100));
        Assert.Equal(int.MaxValue, Snapshot.Differing(expected, smaller));
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(color);
        return bitmap;
    }
}
