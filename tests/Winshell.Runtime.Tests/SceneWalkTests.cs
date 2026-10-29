using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class SceneWalkTests
{
    [Fact]
    public void Paint_order_sorts_by_z_then_declaration_and_follows_changes()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              Item { objectName: "a"; z: 1 }
              Item { objectName: "b" }
              Item { objectName: "c"; z: 1 }
              Item { objectName: "d" }
            }
            """);
        using var _ = shell;
        Assert.Equal("b d a c", Names(root));
        Assert.Same(root.PaintOrder, root.PaintOrder);

        Find(root, "b").WriteProperty("z", 2);
        Assert.Equal("d a c b", Names(root));

        var e = new Item { ObjectName = "e" };
        e.SetParentItem(root);
        Assert.Equal("d e a c b", Names(root));

        Find(root, "a").SetParentItem(null);
        Assert.Equal("d e c b", Names(root));

        Find(root, "c").WriteProperty("z", -1);
        Assert.Equal("c d e b", Names(root));
        GC.KeepAlive(scene);
    }

    [Fact]
    public void Painting_does_not_create_the_layer_property()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Rectangle {
              width: 100; height: 100; color: "red"; opacity: 0.5
              Rectangle { width: 50; height: 50; color: "blue" }
            }
            """);
        using var _ = shell;
        Paint(scene);
        scene.PointerMove(20, 20);

        Assert.Null(scene.Root.CreatedLayer);
        Assert.Null(root.CreatedLayer);
        Assert.Null(root.ChildItems[0].CreatedLayer);
    }

    [Fact]
    public void A_delegate_wholly_outside_a_clipping_list_view_is_not_painted()
    {
        // Each delegate draws a band 50 pixels above itself. The second delegate starts 10 pixels below the view,
        // so its red band would show in the view's lower half if the delegate were painted.
        var (root, scene, shell) = Show(List(clip: true));
        using var _ = shell;
        using var bitmap = Paint(scene);
        Assert.Equal(0, bitmap.GetPixel(50, 75).Alpha);

        root.WriteProperty("contentY", 110);
        shell.Loop.Process();
        using var scrolled = Paint(scene);
        Assert.Equal(SKColors.Blue, scrolled.GetPixel(50, 50));
    }

    [Fact]
    public void A_delegate_outside_a_list_view_that_does_not_clip_is_painted()
    {
        var (_, scene, shell) = Show(List(clip: false));
        using var owned = shell;
        using var bitmap = Paint(scene);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 75));
    }

    private static string List(bool clip) => $$"""
        import QtQuick
        ListView {
          width: 100; height: 100; spacing: 10; clip: {{(clip ? "true" : "false")}}
          model: 3
          delegate: Rectangle {
            required property int index
            width: 100; height: 100
            color: index == 1 ? "blue" : "transparent"
            Rectangle { y: -50; width: 100; height: 50; color: index == 1 ? "red" : "transparent" }
          }
        }
        """;

    private static SKBitmap Paint(Scene scene)
    {
        var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        scene.Paint(canvas, TimeSpan.Zero);
        return bitmap;
    }

    private static string Names(Item item) => string.Join(" ", item.PaintOrder.Select(c => c.ObjectName));

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(100);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static Item Find(Item root, string objectName) => root.ChildItems.Single(c => c.ObjectName == objectName);
}
