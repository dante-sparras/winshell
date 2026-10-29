using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Shapes;

namespace Winshell.Runtime.Tests;

public class ShapePathCacheTests
{
    private static (Shape Shape, TestShell Shell) Load(string body)
    {
        var shell = new TestShell(("shell.qml", $$"""
            import QtQuick
            import QtQuick.Shapes
            Shape {
              {{body}}
            }
            """));
        var shape = (Shape)shell.Load();
        shell.Loop.Process();
        return (shape, shell);
    }

    private static void Paint(Item item)
    {
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        item.Paint(canvas, new PaintContext());
    }

    [Fact]
    public void A_shape_builds_its_path_once_until_the_path_changes()
    {
        var (shape, shell) = Load("""
            ShapePath { startX: 0; startY: 0; PathSvg { path: "L 10 0 L 10 10 Z" } }
            """);
        using var _ = shell;
        var path = shape.Resources.Snapshot.OfType<ShapePath>().Single();
        var built = path.PathBuilds;

        Paint(shape);
        Paint(shape);
        Assert.True(shape.HitTest(5, 5));
        Assert.Equal(built, path.PathBuilds);

        path.StrokeColor = QColor.FromRgb(255, 0, 0);
        Paint(shape);
        Assert.Equal(built, path.PathBuilds);

        path.StartX = 1;
        Paint(shape);
        Paint(shape);
        Assert.Equal(built + 1, path.PathBuilds);

        ((PathSvg)path.PathElements.Snapshot[0]).Path = "L 20 0 L 20 20 Z";
        Paint(shape);
        Assert.Equal(built + 2, path.PathBuilds);
    }

    [Fact]
    public void Fill_contains_misses_the_hole_of_an_odd_even_path()
    {
        var (shape, shell) = Load("""
            containsMode: Shape.FillContains
            ShapePath {
              fillRule: ShapePath.OddEvenFill
              PathRectangle { width: 30; height: 30 }
              PathRectangle { x: 10; y: 10; width: 10; height: 10 }
            }
            """);
        using var _ = shell;
        Assert.True(shape.HitTest(5, 5));
        Assert.False(shape.HitTest(15, 15));

        shape.Resources.Snapshot.OfType<ShapePath>().Single().FillRule = (int)ShapePath.FillRuleValue.WindingFill;
        Assert.True(shape.HitTest(15, 15));
    }

    [Fact]
    public void Fill_contains_follows_the_fill_mode_scale()
    {
        var (shape, shell) = Load("""
            containsMode: Shape.FillContains
            fillMode: Shape.Stretch
            width: 100; height: 100
            ShapePath { strokeWidth: 0; PathRectangle { width: 10; height: 10 } }
            """);
        using var _ = shell;
        Assert.True(shape.HitTest(5, 5));
        Assert.True(shape.HitTest(80, 80));
        Assert.False(shape.HitTest(101, 50));
    }
}
