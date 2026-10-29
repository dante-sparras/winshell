using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>Controls keep their text layouts and shadow blurs between paints instead of making new ones each time.</summary>
public class ControlNativeCacheTests
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

    private static void Paint(Scene scene)
    {
        using var bitmap = new SKBitmap(300, 100);
        using var canvas = new SKCanvas(bitmap);
        scene.Paint(canvas, TimeSpan.Zero);
    }

    [Fact]
    public void A_button_label_elides_its_text_once_until_the_text_changes()
    {
        var label = new IconLabel { Text = "A label far too long for the button", Width = 60, Height = 20 };
        using var bitmap = new SKBitmap(100, 20);
        using var canvas = new SKCanvas(bitmap);

        label.Paint(canvas, new PaintContext());
        label.Paint(canvas, new PaintContext());
        Assert.Equal(1, label.ElidedBuilds);

        label.Text = "Another label far too long for the button";
        label.Paint(canvas, new PaintContext());
        label.Paint(canvas, new PaintContext());
        Assert.Equal(2, label.ElidedBuilds);
    }

    [Fact]
    public void A_text_field_lays_out_its_placeholder_once_until_the_placeholder_changes()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            TextField { width: 200; placeholderText: "Search" }
            """);
        using var _ = shell;
        var field = (TextField)root;

        Paint(scene);
        var built = field.PlaceholderBuilds;
        Paint(scene);
        // A smaller implicit width makes the field measure its placeholder again.
        field.ImplicitWidth = 1;
        Assert.Equal(built, field.PlaceholderBuilds);

        field.PlaceholderText = "Search apps";
        Paint(scene);
        Paint(scene);
        // One layout to measure the new placeholder, one to draw it.
        Assert.Equal(built + 2, field.PlaceholderBuilds);
    }

    [Fact]
    public void A_combo_box_measures_its_entries_once_until_they_change()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ComboBox { model: ["First", "Second"] }
            """);
        using var _ = shell;
        var combo = (ComboBox)root;
        var measured = combo.RowMeasures;
        var width = combo.ImplicitWidth;

        combo.UpdateImplicitSize();
        combo.UpdateImplicitSize();
        Assert.Equal(measured, combo.RowMeasures);

        combo.Model = new List<object?> { "First", "A much longer second entry" };
        combo.UpdateImplicitSize();
        Assert.Equal(measured + 1, combo.RowMeasures);
        Assert.True(combo.ImplicitWidth > width, $"{combo.ImplicitWidth} should be wider than {width}");
    }

    [Fact]
    public void A_shadow_reuses_one_blur_per_sigma()
    {
        Assert.Same(Fluent.ShadowBlur(4), Fluent.ShadowBlur(4));
        Assert.NotSame(Fluent.ShadowBlur(4), Fluent.ShadowBlur(8));
    }
}
