using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>The slider, progress bar and scroll bar against WinUI 3's geometry, colors and motion.</summary>
[Collection(ThemeCollection.Name)]
public class FluentRangeTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(200);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static SKBitmap Paint(Scene scene, TimeSpan time)
    {
        var bitmap = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        scene.Paint(canvas, time);
        return bitmap;
    }

    private static QColor Over(QColor color, QColor background) => QColor.Blend(background, color.WithAlpha(1), color.A);

    private static void AssertNear(QColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.R8 - actual.Red), 0, 2);
        Assert.InRange(Math.Abs(expected.G8 - actual.Green), 0, 2);
        Assert.InRange(Math.Abs(expected.B8 - actual.Blue), 0, 2);
    }

    [Fact]
    public void A_progress_bar_is_a_three_pixel_bar_on_a_one_pixel_track_and_ignores_disabled()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            var (_, scene, shell) = Show("""
                import QtQuick
                import QtQuick.Controls
                Column {
                  ProgressBar { width: 100; value: 0.5 }
                  ProgressBar { width: 100; value: 0.5; enabled: false }
                }
                """);
            using var _ = shell;
            using var bitmap = Paint(scene, TimeSpan.Zero);
            var t = Theme.Current;
            foreach (var top in new[] { 0, 4 })
            {
                // Rows top..top+3: the bar covers 0..2 and the track is row 1 only.
                AssertNear(t.AccentFillColorDefault, bitmap.GetPixel(20, top));
                AssertNear(t.AccentFillColorDefault, bitmap.GetPixel(20, top + 2));
                AssertNear(Over(t.ControlStrongStrokeColorDefault, QColor.Black), bitmap.GetPixel(80, top + 1));
                AssertNear(QColor.Black, bitmap.GetPixel(80, top));
                AssertNear(QColor.Black, bitmap.GetPixel(80, top + 2));
            }
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void A_scroll_bar_widens_400_ms_after_the_pointer_rests_on_it()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            var (root, scene, shell) = Show("""
                import QtQuick
                import QtQuick.Controls
                ScrollBar { height: 160; orientation: Qt.Vertical; size: 0.5; policy: ScrollBar.AlwaysOn }
                """);
            using var _ = shell;
            var bar = (Quick.Controls.ScrollBar)root;
            int ThumbWidth(SKBitmap b) => Enumerable.Range(0, (int)bar.Width).Count(x => b.GetPixel(x, 60).Red > 80);
            var start = TimeSpan.FromSeconds(10);
            using (var rest = Paint(scene, start))
                Assert.Equal(2, ThumbWidth(rest));
            bar.HoverEnter(6, 60, 0);
            Paint(scene, start + TimeSpan.FromMilliseconds(16)).Dispose();
            using (var waiting = Paint(scene, start + TimeSpan.FromMilliseconds(300)))
                Assert.Equal(2, ThumbWidth(waiting));
            Paint(scene, start + TimeSpan.FromMilliseconds(420)).Dispose();
            using (var open = Paint(scene, start + TimeSpan.FromMilliseconds(600)))
                Assert.Equal(6, ThumbWidth(open));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void Pressing_a_scroll_bar_arrow_steps_and_the_thumb_stays_between_the_arrows()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ScrollBar { height: 160; orientation: Qt.Vertical; size: 0.25; position: 0.5; stepSize: 0.1; policy: ScrollBar.AlwaysOn }
            """);
        using var _ = shell;
        var bar = (Quick.Controls.ScrollBar)root;
        // A quarter of the 128 pixels between the two 16 pixel arrow zones.
        Assert.Equal(32, bar.ContentItem!.Height, 3);
        Assert.Equal(16 + 0.5 * 128, bar.ContentItem.Y, 3);
        bar.PointerPress(6, 4, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        bar.PointerRelease(6, 4, QtEnums.LeftButton, 0, 0);
        Assert.Equal(0.4, bar.Position, 3);
        bar.PointerPress(6, 156, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        bar.PointerRelease(6, 156, QtEnums.LeftButton, 0, 0);
        Assert.Equal(0.5, bar.Position, 3);
    }
}
