using System.Globalization;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Text lays out once per change, and only when a change can move its lines.</summary>
public class TextRelayoutTests
{
    private static (T Item, TestShell Shell) Load<T>(string qml)
        where T : Item
    {
        var shell = new TestShell(("shell.qml", qml));
        var item = (T)shell.Load();
        shell.Loop.Process();
        PolishQueue.Flush();
        return (item, shell);
    }

    [Fact]
    public void Moving_a_wrapped_text_lays_it_out_no_more()
    {
        var (text, shell) = Load<Text>("""
            import QtQuick
            Text { width: 80; wrapMode: Text.WordWrap; text: "A sentence long enough to wrap onto several lines" }
            """);
        using var _ = shell;
        var built = text.LayoutBuilds;
        var height = text.ContentHeight;

        for (var i = 1; i <= 10; i++)
        {
            text.X = i;
            text.Y = i;
            PolishQueue.Flush();
        }

        Assert.Equal(height, text.ContentHeight);
        Assert.Equal(built, text.LayoutBuilds);
    }

    [Fact]
    public void A_wrapped_text_lays_out_again_when_its_width_changes()
    {
        var (text, shell) = Load<Text>("""
            import QtQuick
            Text { width: 300; wrapMode: Text.WordWrap; text: "A sentence long enough to wrap onto several lines" }
            """);
        using var _ = shell;
        var lines = text.LineCount;

        text.Width = 80;

        Assert.True(text.LineCount > lines);
    }

    [Fact]
    public void Several_changes_in_one_turn_lay_the_text_out_once()
    {
        var (text, shell) = Load<Text>("""
            import QtQuick
            Text { text: "Hello" }
            """);
        using var _ = shell;
        var built = text.LayoutBuilds;

        shell.Eval(text, "(text = 'Hello there', font.pixelSize = 20, color = 'red')");
        shell.Loop.Process();
        PolishQueue.Flush();

        Assert.Equal(built + 1, text.LayoutBuilds);
    }

    [Fact]
    public void Implicit_width_is_up_to_date_right_after_the_text_changes()
    {
        var (text, shell) = Load<Text>("""
            import QtQuick
            Text { text: "Hi" }
            """);
        using var _ = shell;
        var before = text.ImplicitWidth;

        var after = Convert.ToDouble(shell.Eval(text, "(text = 'A much longer line of text', implicitWidth)"), CultureInfo.InvariantCulture);
        var width = Convert.ToDouble(shell.Eval(text, "width"), CultureInfo.InvariantCulture);
        PolishQueue.Flush();

        Assert.True(after > before);
        Assert.Equal(text.ImplicitWidth, after);
        Assert.Equal(after, width);
    }

    [Fact]
    public void A_binding_on_implicit_width_follows_the_text()
    {
        var (root, shell) = Load<Item>("""
            import QtQuick
            Item {
              property string label: "Hi"
              Rectangle { objectName: "box"; width: inner.implicitWidth + 10; height: 10 }
              Text { id: inner; text: label }
            }
            """);
        using var _ = shell;
        var box = root.ChildItems.First(c => c.ObjectName == "box");
        var before = box.Width;

        shell.Eval(root, "label = 'A much longer line of text'");
        shell.Loop.Process();

        Assert.True(box.Width > before);
        Assert.Equal(((Text)root.ChildItems.Last()).ImplicitWidth + 10, box.Width);
    }

    [Fact]
    public void A_binding_that_reads_the_text_and_its_size_is_no_binding_loop()
    {
        var (root, shell) = Load<Item>("""
            import QtQuick
            Item {
              property string label: "Hi"
              Text { id: inner; text: label }
              Rectangle { objectName: "box"; width: inner.implicitWidth + label.length; height: 10 }
            }
            """);
        using var _ = shell;
        var box = root.ChildItems.First(c => c.ObjectName == "box");
        var inner = (Text)root.ChildItems.First();

        shell.Eval(root, "label = 'A much longer line of text'");
        shell.Loop.Process();

        Assert.Empty(shell.Messages);
        Assert.Equal(inner.ImplicitWidth + 26, box.Width);
    }

    [Fact]
    public void On_completed_sees_the_measured_text()
    {
        var (root, shell) = Load<Item>("""
            import QtQuick
            Item {
              property real seen
              Rectangle { id: box; width: inner.implicitWidth; height: 10 }
              Text { id: inner; text: "Some text"; font.pixelSize: 20 }
              Component.onCompleted: seen = box.width
            }
            """);
        using var _ = shell;
        var inner = (Text)root.ChildItems.Last();

        Assert.True(inner.ImplicitWidth > 0);
        Assert.Equal(inner.ImplicitWidth, Convert.ToDouble(shell.Eval(root, "seen"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_text_input_measures_its_line_height_once_per_font()
    {
        var (input, shell) = Load<TextInput>("""
            import QtQuick
            TextInput { text: "Hello" }
            """);
        using var __ = shell;
        _ = input.ContentHeight;
        var built = input.LineHeightBuilds;

        _ = input.ContentHeight;
        _ = input.ContentHeight;
        Assert.Equal(built, input.LineHeightBuilds);

        input.Font.PixelSize = 30;
        var height = input.ContentHeight;
        _ = input.ContentHeight;
        Assert.Equal(built + 1, input.LineHeightBuilds);
        Assert.True(height > 30);
    }
}
