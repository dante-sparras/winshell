using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class RightToLeftTextTests
{
    private static TextLayout Layout(string text, TextLayout.Wrap wrap = TextLayout.Wrap.NoWrap, double maxWidth = double.NaN) =>
        TextLayout.Build(
            [new TextSpan(text, new FontSpec("Segoe UI Variable", 14, 400, false, false, false, 0, 0))],
            maxWidth,
            wrap,
            TextLayout.Elide.None,
            0,
            1,
            true);

    [Theory]
    [InlineData("مرحبا بالعالم")]
    [InlineData("שלום עולם")]
    public void Right_to_left_text_has_a_width(string text)
    {
        using var layout = Layout(text);
        Assert.True(layout.Width > 40, $"width {layout.Width}");
        Assert.True(layout.Lines[0].Width > 40, $"line width {layout.Lines[0].Width}");
    }

    [Fact]
    public void Wrapped_right_to_left_lines_have_a_width()
    {
        using var layout = Layout("مرحبا بالعالم مرحبا بالعالم مرحبا بالعالم", TextLayout.Wrap.WordWrap, 80);
        Assert.True(layout.Lines.Count > 1, $"lines {layout.Lines.Count}");
        Assert.All(layout.Lines, line => Assert.True(line.Width > 10, $"line width {line.Width}"));
    }

    [Fact]
    public void Mixed_text_measures_both_directions()
    {
        using var latin = Layout("abc");
        using var mixed = Layout("abc שלום");
        Assert.True(mixed.Width > latin.Width + 20, $"{mixed.Width} vs {latin.Width}");
    }

    [Fact]
    public void Is_right_to_left_follows_the_first_strong_character()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { text: "123 שלום world عربي" }
            """));
        var field = shell.Load();

        Assert.Equal(true, shell.Eval(field, "isRightToLeft(0, text.length)"));
        Assert.Equal(true, shell.Eval(field, "isRightToLeft(4, 8)"));
        Assert.Equal(false, shell.Eval(field, "isRightToLeft(9, text.length)"));
        Assert.Equal(false, shell.Eval(field, "isRightToLeft(0, 3)"));
        Assert.Equal(true, shell.Eval(field, "isRightToLeft(14, text.length)"));
        Assert.Equal(false, shell.Eval(field, "isRightToLeft(8, 4)"));
    }
}
