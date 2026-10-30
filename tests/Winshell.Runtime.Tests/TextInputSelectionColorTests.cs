using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Selected text is drawn in <c>selectedTextColor</c>, and a theme change keeps the colors QML set.</summary>
[Collection(ThemeCollection.Name)]
public class TextInputSelectionColorTests
{
    [Fact]
    public void Selected_text_is_drawn_in_the_selected_text_color()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput {
                width: 280; height: 60
                font.pixelSize: 40
                text: "MMMMMM"
                color: "#ff0000"
                selectionColor: "#000000"
                selectedTextColor: "#00ff00"
                persistentSelection: true
            }
            """));
        var field = (TextInput)shell.Load();
        using var _ = shell;
        field.Select(0, 3);
        var split = (int)Math.Round(field.PositionToRectangle(3).X);

        using var bitmap = new SKBitmap(new SKImageInfo(300, 60, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            field.Paint(canvas, new PaintContext());
        }

        bool Green(SKColor c) => c.Green > 160 && c.Red < 60;
        bool Red(SKColor c) => c.Red > 160 && c.Green < 60;
        var selected = Pixels(bitmap, 0, split - 1);
        var unselected = Pixels(bitmap, split + 1, 300);
        Assert.Contains(selected, Green);
        Assert.DoesNotContain(selected, Red);
        Assert.Contains(unselected, Red);
        Assert.DoesNotContain(unselected, Green);
    }

    [Fact]
    public void A_theme_change_keeps_the_colors_qml_set()
    {
        var before = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(false);
            var shell = new TestShell(("shell.qml", """
                import QtQuick
                Item {
                    property alias set: set
                    property alias unset: unset
                    TextInput { id: set; color: "#123456"; selectionColor: "#234567"; selectedTextColor: "#345678" }
                    TextInput { id: unset }
                }
                """));
            var root = (Item)shell.Load();
            using var _ = shell;
            var set = (TextInput)root.ReadProperty("set")!;
            var unset = (TextInput)root.ReadProperty("unset")!;

            Theme.Current = Theme.Default(true);

            Assert.Equal(QColor.Parse("#123456"), set.Color);
            Assert.Equal(QColor.Parse("#234567"), set.SelectionColor);
            Assert.Equal(QColor.Parse("#345678"), set.SelectedTextColor);
            Assert.Equal(Theme.Current.TextFillColorPrimary, unset.Color);
            Assert.Equal(Theme.Current.AccentFillColorDefault, unset.SelectionColor);
            Assert.Equal(Theme.Current.TextOnAccentFillColorPrimary, unset.SelectedTextColor);
        }
        finally
        {
            Theme.Current = before;
        }
    }

    private static List<SKColor> Pixels(SKBitmap bitmap, int fromX, int toX)
    {
        var pixels = new List<SKColor>();
        for (var x = Math.Max(0, fromX); x < Math.Min(bitmap.Width, toX); x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
                pixels.Add(bitmap.GetPixel(x, y));
        }
        return pixels;
    }
}
