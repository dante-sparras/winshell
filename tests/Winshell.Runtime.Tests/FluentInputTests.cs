using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>The text field, combo box and list item against WinUI 3's text box, combo box and list view item.</summary>
[Collection(ThemeCollection.Name)]
public class FluentInputTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(300);
        scene.Root.SetLayoutHeight(300);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    [Fact]
    public void A_focused_text_box_with_text_has_a_clear_button_that_clears_it()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            TextField {
              width: 200; height: 32; text: "Hello"
              property int edits: 0
              onTextEdited: edits++
            }
            """);
        using var _ = shell;
        var field = (TextField)root;
        // Not focused: the button is hidden and a press there places the caret.
        scene.PointerPress(185, 16, QtEnums.LeftButton);
        scene.PointerRelease(185, 16, QtEnums.LeftButton);
        Assert.Equal("Hello", field.Text_);
        Assert.True(field.ActiveFocus);

        // Once drawn, WinUI's 30 pixel button at the right of the focused box clears it, as a user edit.
        using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(300, 300)))
            scene.Paint(surface.Canvas, TimeSpan.Zero);
        scene.PointerPress(185, 16, QtEnums.LeftButton);
        scene.PointerRelease(185, 16, QtEnums.LeftButton);
        Assert.Equal("", field.Text_);
        Assert.Equal(1, root.ReadProperty("edits"));
        Assert.True(field.ActiveFocus);
    }

    [Fact]
    public void Disabled_text_is_dimmed_unless_qml_set_a_color()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            var (root, _, shell) = Show("""
                import QtQuick
                import QtQuick.Controls
                Column {
                  TextField { text: "A"; enabled: false }
                  TextField { text: "B"; enabled: false; color: "red" }
                  ComboBox { model: ["C"]; enabled: false }
                }
                """);
            using var _ = shell;
            var plain = (TextField)root.ChildItems[0];
            var colored = (TextField)root.ChildItems[1];
            var combo = (Quick.Controls.ComboBox)root.ChildItems[2];
            Assert.Equal(Theme.Current.TextFillColorDisabled, Paint(plain));
            Assert.Equal(QColor.Parse("red"), Paint(colored));
            Assert.Equal(Theme.Current.TextFillColorDisabled, ((Text)combo.ContentItem!).Color);
        }
        finally
        {
            Theme.Current = saved;
        }

        static QColor Paint(TextField field) =>
            (QColor)typeof(TextInput).GetProperty("PaintColor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(field)!;
    }

    [Fact]
    public void A_list_row_is_40_pixels_and_its_text_stays_primary_when_pressed()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ItemDelegate { text: "Row"; down: true }
            """);
        using var _ = shell;
        var row = (ItemDelegate)root;
        Assert.Equal(40, row.ImplicitHeight);
        Assert.Equal(Theme.Current.TextFillColorPrimary, row.ContentColor);
    }
}
