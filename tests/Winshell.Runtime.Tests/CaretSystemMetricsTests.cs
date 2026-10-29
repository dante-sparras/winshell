using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>The caret blinks, stops blinking, and is as wide as the Windows settings in the theme say.</summary>
[Collection(ThemeCollection.Name)]
public class CaretSystemMetricsTests
{
    [Fact]
    public void The_caret_blinks_at_the_theme_blink_time()
    {
        WithTheme(Theme.Default(false) with { CaretBlinkTime = 200, CaretTimeout = 100_000 }, (field, shell) =>
        {
            Assert.True(CaretShown(field));
            shell.Loop.Advance(TimeSpan.FromMilliseconds(190));
            Assert.True(CaretShown(field));
            shell.Loop.Advance(TimeSpan.FromMilliseconds(20));
            Assert.False(CaretShown(field));
            shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
            Assert.True(CaretShown(field));
        });
    }

    [Fact]
    public void The_caret_is_the_theme_caret_width()
    {
        WithTheme(Theme.Default(false) with { CaretWidth = 3 }, (field, _) => Assert.Equal(3, CaretColumns(field)));
        WithTheme(Theme.Default(false) with { CaretWidth = 1 }, (field, _) => Assert.Equal(1, CaretColumns(field)));
    }

    [Fact]
    public void The_caret_stops_blinking_shown_after_the_timeout()
    {
        WithTheme(Theme.Default(false) with { CaretBlinkTime = 100, CaretTimeout = 1000 }, (field, shell) =>
        {
            shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
            Assert.False(CaretShown(field));
            shell.Loop.Advance(TimeSpan.FromMilliseconds(900));
            for (var i = 0; i < 20; i++)
            {
                Assert.True(CaretShown(field));
                shell.Loop.Advance(TimeSpan.FromMilliseconds(50));
            }

            // Typing starts it blinking again. Clearing the text again leaves only the caret to see.
            field.InsertText("a");
            field.Clear();
            shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
            Assert.False(CaretShown(field));
        });
    }

    [Fact]
    public void A_caret_that_does_not_blink_is_always_shown()
    {
        WithTheme(Theme.Default(false) with { CaretBlinkTime = Theme.CaretNoBlink }, (field, shell) =>
        {
            for (var i = 0; i < 100; i++)
            {
                Assert.True(CaretShown(field));
                shell.Loop.Advance(TimeSpan.FromMilliseconds(100));
            }
        });
    }

    private static void WithTheme(Theme theme, Action<TextInput, TestShell> test)
    {
        var before = Theme.Current;
        try
        {
            Theme.Current = theme;
            var shell = new TestShell(("shell.qml", """
                import QtQuick
                Item {
                    property Item field: input
                    TextInput { id: input; width: 100; height: 30; color: "#ffffff"; focus: true }
                }
                """));
            using var _ = shell;
            var root = (Item)shell.Load();
            var scene = new Scene();
            root.SetParentItem(scene.Root);
            shell.Loop.Process();
            var field = (TextInput)root.ReadProperty("field")!;
            field.ForceActiveFocus();
            Assert.True(field.ActiveFocus);
            test(field, shell);
        }
        finally
        {
            Theme.Current = before;
        }
    }

    private static bool CaretShown(TextInput field) => CaretColumns(field) > 0;

    /// <summary>How many pixel columns the caret covers, on a black canvas in a white text color.</summary>
    private static int CaretColumns(TextInput field)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(100, 30, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            field.Paint(canvas, new PaintContext());
        }
        var columns = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).Red > 128))
                columns++;
        }
        return columns;
    }
}
