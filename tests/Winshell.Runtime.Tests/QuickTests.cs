using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class QuickTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml, double width = 200, double height = 100)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(width);
        scene.Root.SetLayoutHeight(height);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static SKBitmap Render(Scene scene, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        scene.Paint(canvas, TimeSpan.Zero);
        return bitmap;
    }

    private static Item Find(Item root, string objectName)
    {
        if (root.ObjectName == objectName)
            return root;
        foreach (var child in root.ChildItems)
        {
            try
            {
                return Find(child, objectName);
            }
            catch (InvalidOperationException)
            {
            }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }

    [Fact]
    public void A_polish_request_is_flushed_by_its_loop_even_when_another_loop_never_ran()
    {
        var abandoned = new ManualEventLoop();
        var loop = new ManualEventLoop();
        var first = new Polishable();
        var second = new Polishable();
        PolishQueue.Request(first, abandoned);
        PolishQueue.Request(second, loop);
        loop.Process();
        // Before, the flush waited on the abandoned loop, and the queue kept both items for good.
        Assert.Equal(1, second.Polished);
        Assert.Equal(1, first.Polished);
    }

    private sealed class Polishable : IPolishable
    {
        public int Polished { get; private set; }

        public void UpdatePolish() => Polished++;
    }

    [Fact]
    public void Anchors_fill_center_and_margins()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              width: 200; height: 100
              Rectangle { objectName: "fill"; anchors.fill: parent; anchors.margins: 10 }
              Rectangle { objectName: "center"; width: 20; height: 10; anchors.centerIn: parent }
              Rectangle { objectName: "right"; width: 30; height: 10; anchors.right: parent.right; anchors.bottom: parent.bottom }
              Rectangle { id: a; objectName: "a"; width: 40; height: 10 }
              Rectangle { objectName: "b"; width: 10; height: 10; anchors.left: a.right; anchors.leftMargin: 5; anchors.verticalCenter: a.verticalCenter }
            }
            """);
        using var _ = shell;
        var fill = Find(root, "fill");
        Assert.Equal((10.0, 10.0, 180.0, 80.0), (fill.X, fill.Y, fill.Width, fill.Height));
        var center = Find(root, "center");
        Assert.Equal((90.0, 45.0), (center.X, center.Y));
        var right = Find(root, "right");
        Assert.Equal((170.0, 90.0), (right.X, right.Y));
        var b = Find(root, "b");
        Assert.Equal((45.0, 0.0), (b.X, b.Y));
        root.Width = 300;
        Assert.Equal(280.0, fill.Width);
        Assert.Equal(270.0, right.X);
    }

    [Fact]
    public void Row_and_Column_place_children_and_size_themselves()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              Row {
                objectName: "row"
                spacing: 5
                Rectangle { width: 10; height: 20 }
                Rectangle { width: 30; height: 10 }
                Rectangle { width: 30; height: 10; visible: false }
                Rectangle { objectName: "last"; width: 15; height: 5 }
              }
              Column {
                objectName: "col"
                y: 50
                spacing: 2
                Rectangle { width: 10; height: 20 }
                Rectangle { objectName: "second"; width: 30; height: 10 }
              }
            }
            """);
        using var _ = shell;
        var row = Find(root, "row");
        Assert.Equal((65.0, 20.0), (row.Width, row.Height));
        Assert.Equal(50.0, Find(root, "last").X);
        var col = Find(root, "col");
        Assert.Equal((30.0, 32.0), (col.Width, col.Height));
        Assert.Equal(22.0, Find(root, "second").Y);
    }

    [Fact]
    public void RowLayout_shares_space_with_items_that_fill()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Layouts
            RowLayout {
              width: 200; height: 30
              spacing: 0
              Rectangle { objectName: "fixed"; implicitWidth: 50; implicitHeight: 10 }
              Rectangle { objectName: "grow"; Layout.fillWidth: true; Layout.fillHeight: true }
              Rectangle { objectName: "pref"; Layout.preferredWidth: 30; implicitHeight: 10 }
            }
            """);
        using var _ = shell;
        var fixedItem = Find(root, "fixed");
        var grow = Find(root, "grow");
        var pref = Find(root, "pref");
        Assert.Equal((0.0, 50.0), (fixedItem.X, fixedItem.Width));
        Assert.Equal((50.0, 120.0, 30.0), (grow.X, grow.Width, grow.Height));
        Assert.Equal((170.0, 30.0, 10.0), (pref.X, pref.Width, fixedItem.Height));
        Assert.Equal(10.0, fixedItem.Y);
    }

    [Fact]
    public void Repeater_makes_a_delegate_per_row()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Row {
              property var names: ["a", "b", "c"]
              Repeater {
                model: names
                Text { required property string modelData; required property int index; text: index + modelData }
              }
            }
            """);
        using var _ = shell;
        var texts = root.ChildItems.OfType<Text>().Select(t => t.Text_).ToList();
        Assert.Equal(["0a", "1b", "2c"], texts);
        shell.Eval(root, "names = ['a', 'c']");
        PolishQueue.Flush();
        Assert.Equal(["0a", "1c"], root.ChildItems.OfType<Text>().Select(t => t.Text_));
    }

    [Theory]
    [InlineData("Column")]
    [InlineData("Grid { columns: 1 }")]
    [InlineData("ColumnLayout { spacing: 0 }")]
    public void Positioners_and_layouts_follow_a_repeater_that_reorders_its_rows(string container)
    {
        var open = container.Contains('{', StringComparison.Ordinal) ? container[..^1] : container + " {";
        var (root, _, shell) = Show($$"""
            import QtQuick
            import QtQuick.Layouts
            {{open}}
              property var names: ["a", "b", "c"]
              Repeater {
                model: names
                Rectangle { required property string modelData; objectName: modelData; implicitWidth: 10; implicitHeight: 10 }
              }
            }
            """);
        using var _ = shell;
        Assert.Equal([0.0, 10.0, 20.0], new[] { "a", "b", "c" }.Select(n => Find(root, n).Y));
        var kept = Find(root, "a");
        shell.Eval(root, "names = ['c', 'a', 'b']");
        shell.Loop.Process();
        PolishQueue.Flush();
        // The same delegates, moved to their new rows.
        Assert.Same(kept, Find(root, "a"));
        Assert.Equal([0.0, 10.0, 20.0], new[] { "c", "a", "b" }.Select(n => Find(root, n).Y));
    }

    [Fact]
    public void Text_measures_its_content()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              Text { objectName: "t"; text: "hello world"; font.pixelSize: 20 }
              Text { objectName: "wrap"; width: 60; wrapMode: Text.WordWrap; text: "hello world again" }
              Text { objectName: "elide"; width: 40; elide: Text.ElideRight; text: "hello world again" }
            }
            """);
        using var _ = shell;
        var t = Find(root, "t");
        Assert.InRange(t.ImplicitWidth, 70, 160);
        Assert.InRange(t.ImplicitHeight, 20, 32);
        var wrap = (Text)Find(root, "wrap");
        Assert.True(wrap.LineCount >= 2, $"lineCount {wrap.LineCount}");
        Assert.True(wrap.Height > t.Height / 20 * 14);
        var elide = (Text)Find(root, "elide");
        Assert.True(elide.Truncated);
    }

    [Fact]
    public void Rectangles_draw_their_color()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            Item {
              width: 20; height: 20
              Rectangle { anchors.fill: parent; color: "#ff0000" }
              Rectangle { x: 10; width: 10; height: 10; color: "blue"; opacity: 0.5 }
            }
            """, 20, 20);
        using var _ = shell;
        using var bitmap = Render(scene, 20, 20);
        Assert.Equal(new SKColor(255, 0, 0, 255), bitmap.GetPixel(2, 15));
        var blended = bitmap.GetPixel(15, 5);
        Assert.InRange(blended.Red, 120, 135);
        Assert.InRange(blended.Blue, 120, 135);
    }

    [Fact]
    public void MouseArea_gets_clicks_and_hover()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              width: 100; height: 100
              property int clicks: 0
              property bool hovered: area.containsMouse
              MouseArea { id: area; x: 10; y: 10; width: 50; height: 50; hoverEnabled: true; onClicked: mouse => clicks += mouse.x > 0 ? 1 : 0 }
              Rectangle { x: 40; y: 40; width: 50; height: 50 }
            }
            """, 100, 100);
        using var _ = shell;
        scene.PointerMove(20, 20);
        Assert.True((bool)root.ReadProperty("hovered")!);
        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(1, root.ReadProperty("clicks"));
        // Hover passes through the rectangle on top, which does not take hover.
        scene.PointerMove(45, 45);
        Assert.True((bool)root.ReadProperty("hovered")!);
        scene.PointerMove(95, 95);
        Assert.False((bool)root.ReadProperty("hovered")!);
    }

    [Fact]
    public void Behavior_animates_changes_after_creation()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Rectangle {
              width: 10
              Behavior on width { NumberAnimation { duration: 100 } }
            }
            """);
        using var _ = shell;
        Assert.Equal(10.0, root.Width);
        root.WriteProperty("width", 110);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(50));
        Assert.InRange(root.Width, 30, 90);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(110.0, root.Width);
    }

    [Fact]
    public void Loader_creates_its_component_and_follows_its_size()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            Item {
              property bool on: true
              Loader {
                objectName: "loader"
                active: on
                sourceComponent: Rectangle { implicitWidth: 33; implicitHeight: 11 }
              }
            }
            """);
        using var _ = shell;
        var loader = (Loader)Find(root, "loader");
        Assert.NotNull(loader.Item_);
        Assert.Equal((33.0, 11.0), (loader.Width, loader.Height));
        root.WriteProperty("on", false);
        Assert.Null(loader.Item_);
    }

    [Fact]
    public void A_canvas_paints_again_at_a_new_pixel_ratio()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Canvas { width: 50; height: 50; property int paints: 0; onPaint: paints++ }
            """);
        using var _ = shell;
        Render(scene, 50, 50).Dispose();
        shell.Loop.Process();
        var before = Convert.ToInt32(root.ReadProperty("paints"));

        // The window moved to a 200% monitor: the canvas surface is made again at twice the pixels, and painted again.
        scene.DevicePixelRatio = 2;
        shell.Loop.Process();
        Render(scene, 100, 100).Dispose();
        Assert.True(Convert.ToInt32(root.ReadProperty("paints")) > before);
    }
}
