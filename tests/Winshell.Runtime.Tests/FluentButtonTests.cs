using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>The buttons, check boxes, radio buttons and toggle switch against WinUI 3's sizes, colors and motion.</summary>
[Collection(ThemeCollection.Name)]
public class FluentButtonTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(300);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static SKBitmap Paint(Scene scene, TimeSpan time, out PaintContext context)
    {
        var bitmap = new SKBitmap(new SKImageInfo(300, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        context = scene.Paint(canvas, time);
        return bitmap;
    }

    private static QColor Pixel(SKBitmap bitmap, int x, int y)
    {
        var c = bitmap.GetPixel(x, y);
        return QColor.FromRgb(c.Red, c.Green, c.Blue, c.Alpha);
    }

    [Fact]
    public void A_button_is_its_text_plus_winui_padding_and_32_pixels_high()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Button { text: "Save changes" }
            """);
        using var _ = shell;
        var button = (Quick.Controls.Button)root;
        // WinUI's padding is 11,5,11,6 inside a 1 pixel border.
        Assert.Equal(button.ContentItem!.ImplicitWidth + 24, button.ImplicitWidth);
        Assert.Equal(32, button.ImplicitHeight);
    }

    [Fact]
    public void A_transition_eases_fast_out_slow_in_and_asks_for_frames_until_it_arrives()
    {
        var slide = new NumberTransition(100);
        var start = TimeSpan.FromSeconds(10);
        Assert.Equal(0, slide.Step(0, new PaintContext { Time = start }));

        var first = new PaintContext { Time = start };
        Assert.Equal(0, slide.Step(1, first));
        Assert.True(first.WantsAnotherFrame);

        var half = new PaintContext { Time = start + TimeSpan.FromMilliseconds(50) };
        var middle = slide.Step(1, half);
        Assert.InRange(middle, 0.75, 0.99);
        Assert.True(half.WantsAnotherFrame);

        var end = new PaintContext { Time = start + TimeSpan.FromMilliseconds(100) };
        Assert.Equal(1, slide.Step(1, end));
        Assert.False(end.WantsAnotherFrame);

        // A paint without a clock jumps.
        Assert.Equal(0, slide.Step(0, new PaintContext()));
    }

    [Fact]
    public void The_toggle_switch_knob_slides_across_when_switched_on()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(false);
            var (root, scene, shell) = Show("""
                import QtQuick
                import QtQuick.Controls
                Switch { }
                """);
            using var _ = shell;
            var start = TimeSpan.FromSeconds(10);
            Paint(scene, start, out var _).Dispose();
            root.WriteProperty("checked", true);
            shell.Loop.Process();
            // The first paint after the change starts the transitions.
            Paint(scene, start + TimeSpan.FromMilliseconds(16), out var _).Dispose();

            // The knob is white on the accent track once on. Find its middle along the track's center line.
            double KnobX(SKBitmap b)
            {
                var y = (int)(root.Height / 2);
                var xs = Enumerable.Range(0, 40).Where(x => Pixel(b, x, y) is var c && c.R8 > 240 && c.G8 > 240 && c.B8 > 240).ToList();
                return xs.Count == 0 ? double.NaN : xs.Average();
            }
            using var early = Paint(scene, start + TimeSpan.FromMilliseconds(100), out var moving);
            using var late = Paint(scene, start + TimeSpan.FromMilliseconds(400), out var settled);
            Assert.True(moving.WantsAnotherFrame);
            Assert.False(settled.WantsAnotherFrame);
            Assert.InRange(KnobX(early), 15, 29);
            Assert.InRange(KnobX(late), 29, 31);
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void In_a_contrast_theme_an_accent_button_is_highlight_and_a_checked_box_draws_its_border()
    {
        var saved = Theme.Current;
        try
        {
            var system = SystemColors.Default with { Highlight = QColor.Parse("#1aebff"), ButtonFace = QColor.Parse("#000000") };
            Theme.Current = Theme.Default(true) with { System = system, IsHighContrast = true };
            Assert.Equal(system.Highlight, FluentResources.AccentButtonBackground(Theme.Current).Rest);
            Assert.Equal(system.HighlightText, FluentResources.AccentButtonForeground(Theme.Current).Rest);
            Assert.Equal(system.Highlight, FluentResources.CheckBoxCheckBackgroundFillChecked(Theme.Current).Rest);
            var (_, scene, shell) = Show("""
                import QtQuick
                import QtQuick.Controls
                Button { text: "OK"; highlighted: true }
                """);
            using var _ = shell;
            using var bitmap = Paint(scene, TimeSpan.Zero, out var _);
            Assert.Equal(system.Highlight, Pixel(bitmap, 4, 4));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void Check_box_text_does_not_dim_when_pressed_but_button_text_does()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Row {
              CheckBox { objectName: "box"; text: "A"; down: true }
              Button { objectName: "button"; text: "B"; down: true }
            }
            """);
        using var _ = shell;
        var box = (AbstractButton)root.ChildItems[0];
        var button = (AbstractButton)root.ChildItems[1];
        Assert.Equal(Theme.Current.TextFillColorPrimary, box.ContentColor);
        Assert.Equal(Theme.Current.TextFillColorSecondary, button.ContentColor);
    }
}
