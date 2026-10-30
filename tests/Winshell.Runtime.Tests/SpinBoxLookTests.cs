using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>The spin box against WinUI 3's number box with its spin buttons inline.</summary>
[Collection(ThemeCollection.Name)]
public class SpinBoxLookTests
{
    private const string Qml = """
        import QtQuick
        import QtQuick.Controls
        Item {
          SpinBox { x: 10; y: 10; width: 160; value: 5 }
        }
        """;

    private static (SpinBox Spin, Scene Scene, TestShell Shell) Show()
    {
        var shell = new TestShell(("shell.qml", Qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return ((SpinBox)root.ChildItems[0], scene, shell);
    }

    private static SKBitmap Paint(Scene scene)
    {
        var bitmap = new SKBitmap(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        scene.Paint(canvas, TimeSpan.Zero);
        return bitmap;
    }

    private static void WithTheme(Action test)
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            test();
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void A_spin_box_is_a_32_pixel_field_with_its_buttons_inside_at_the_right() => WithTheme(() =>
    {
        var (spin, scene, shell) = Show();
        using var _ = shell;
        Assert.Equal(32, spin.Height);

        // Down is the rightmost button, up is left of it, and the rest of the field holds the value.
        scene.PointerPress(10 + 160 - 20, 26, QtEnums.LeftButton);
        scene.PointerRelease(10 + 160 - 20, 26, QtEnums.LeftButton);
        Assert.Equal(4, spin.Value);
        scene.PointerPress(10 + 160 - 60, 26, QtEnums.LeftButton);
        scene.PointerRelease(10 + 160 - 60, 26, QtEnums.LeftButton);
        Assert.Equal(5, spin.Value);
        scene.PointerPress(30, 26, QtEnums.LeftButton);
        scene.PointerRelease(30, 26, QtEnums.LeftButton);
        Assert.Equal(5, spin.Value);
        var text = (Text)spin.ContentItem!;
        Assert.True(text.X + text.Width <= 160 - 80, "the value stays left of the buttons");

        // A hovered button lights up inside the field.
        spin.HoverMove(160 - 72, 6, 0);
        using var hovered = Paint(scene);
        spin.HoverMove(30, 16, 0);
        using var rest = Paint(scene);
        Assert.NotEqual(rest.GetPixel(10 + 160 - 72, 10 + 6), hovered.GetPixel(10 + 160 - 72, 10 + 6));
        Assert.Equal(rest.GetPixel(10 + 160 - 72, 10 + 1), hovered.GetPixel(10 + 160 - 72, 10 + 1));
    });

    [Fact]
    public void A_focused_spin_box_has_the_text_box_accent_line_along_its_bottom() => WithTheme(() =>
    {
        var (spin, scene, shell) = Show();
        using var _ = shell;
        using (var unfocused = Paint(scene))
            Assert.NotEqual(Theme.Current.AccentFillColorDefault.ToSk(), unfocused.GetPixel(40, 10 + 31));

        spin.ForceActiveFocus();
        using var focused = Paint(scene);
        var accent = Theme.Current.AccentFillColorDefault.ToSk();
        Assert.Equal(accent, focused.GetPixel(40, 10 + 31));
        Assert.Equal(accent, focused.GetPixel(40, 10 + 30));
        Assert.NotEqual(accent, focused.GetPixel(40, 10 + 29));
    });

    [Fact]
    public void A_spin_box_with_keyboard_focus_shows_the_accent_line_and_no_focus_rectangle() => WithTheme(() =>
    {
        var (spin, scene, shell) = Show();
        using var _ = shell;
        scene.MoveFocus(spin);
        using var bitmap = Paint(scene);
        Assert.Equal(SKColors.Black, bitmap.GetPixel(40, 10 - 2));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(40, 10 + 32 + 1));
    });
}
