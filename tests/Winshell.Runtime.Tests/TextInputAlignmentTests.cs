using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Clicks, <c>cursorRectangle</c> and the IME caret follow the text where alignment draws it.</summary>
public class TextInputAlignmentTests
{
    private static (TextInput Field, Scene Scene, TestShell Shell) Show(string alignment)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            TextInput { width: 200; height: 60; text: "abc"; color: "black"; font.pixelSize: 16; {{alignment}} }
            """));
        var field = (TextInput)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(300);
        scene.Root.SetLayoutHeight(300);
        field.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (field, scene, shell);
    }

    private static void Click(Scene scene, double x, double y)
    {
        scene.PointerPress(x, y, QtEnums.LeftButton);
        scene.PointerRelease(x, y, QtEnums.LeftButton);
    }

    /// <summary>The column the caret is drawn in: what changes when the caret is hidden.</summary>
    private static int CaretColumn(TextInput field)
    {
        using var shown = Render(field);
        field.CursorVisible = false;
        using var hidden = Render(field);
        field.CursorVisible = true;
        for (var x = 0; x < shown.Width; x++)
        {
            for (var y = 0; y < shown.Height; y++)
            {
                if (shown.GetPixel(x, y) != hidden.GetPixel(x, y))
                    return x;
            }
        }
        throw new Xunit.Sdk.XunitException("no caret was drawn");
    }

    private static SKBitmap Render(TextInput field)
    {
        var bitmap = new SKBitmap(200, 60);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        field.Paint(canvas, new PaintContext());
        return bitmap;
    }

    [Theory]
    [InlineData("horizontalAlignment: Text.AlignRight")]
    [InlineData("horizontalAlignment: Text.AlignHCenter")]
    public void A_click_left_of_the_text_puts_the_cursor_at_the_start_and_one_at_the_right_end_at_the_end(string alignment)
    {
        var (field, scene, shell) = Show(alignment);
        using var _ = shell;
        var textLeft = alignment.EndsWith("AlignRight") ? 200 - field.ContentWidth : (200 - field.ContentWidth) / 2;

        Click(scene, textLeft - 3, 10);
        Assert.Equal(0, field.CursorPosition);

        Click(scene, 199, 10);
        Assert.Equal(3, field.CursorPosition);
    }

    [Theory]
    [InlineData("horizontalAlignment: Text.AlignLeft", 0)]
    [InlineData("horizontalAlignment: Text.AlignLeft", 2)]
    [InlineData("horizontalAlignment: Text.AlignRight", 0)]
    [InlineData("horizontalAlignment: Text.AlignRight", 2)]
    [InlineData("horizontalAlignment: Text.AlignHCenter", 0)]
    [InlineData("horizontalAlignment: Text.AlignHCenter", 2)]
    public void The_cursor_rectangle_is_where_the_caret_is_drawn(string alignment, int position)
    {
        var (field, _, shell) = Show(alignment);
        using var _shell = shell;
        field.ForceActiveFocus();
        field.CursorPosition = position;

        var drawn = CaretColumn(field);

        Assert.InRange(field.CursorRectangle.X, drawn - 1, drawn + 1);
        Assert.Equal(field.CursorRectangle.X, field.PositionToRectangle(position).X);
        Assert.Equal(field.CursorRectangle, field.ImeCaret());
    }

    [Fact]
    public void Vertical_alignment_moves_the_cursor_rectangle()
    {
        var (top, _, topShell) = Show("");
        using var _top = topShell;
        var (centred, _, centredShell) = Show("verticalAlignment: Text.AlignVCenter");
        using var _centred = centredShell;
        var (bottom, _, bottomShell) = Show("verticalAlignment: Text.AlignBottom");
        using var _bottom = bottomShell;

        var height = top.CursorRectangle.Height;
        Assert.Equal(0, top.CursorRectangle.Y);
        Assert.Equal((60 - height) / 2, centred.CursorRectangle.Y, 3);
        Assert.Equal(60 - height, bottom.CursorRectangle.Y, 3);
        Assert.Equal(centred.CursorRectangle, centred.ImeCaret());
    }
}
