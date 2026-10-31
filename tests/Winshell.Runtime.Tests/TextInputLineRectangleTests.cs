using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A caret on a later line is reported on that line.</summary>
public class TextInputLineRectangleTests
{
    [Fact]
    public void A_position_on_the_second_line_reports_that_lines_y()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { width: 200; height: 80; text: "alpha\nbeta" }
            """));
        var field = (TextInput)shell.Load();
        shell.Loop.Process();
        PolishQueue.Flush();

        var first = field.PositionToRectangle(0);
        var second = field.PositionToRectangle(field.Text_.IndexOf('b'));
        Assert.True(second.Y > first.Y, $"first {first.Y} second {second.Y}");
    }

    [Fact]
    public void An_empty_field_reports_a_caret_a_line_high()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            TextInput { width: 200; height: 80 }
            """));
        var field = (TextInput)shell.Load();
        shell.Loop.Process();
        PolishQueue.Flush();

        Assert.True(field.PositionToRectangle(0).Height > 0);
    }
}
