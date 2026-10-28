using SkiaSharp;

namespace Winshell.Runtime.Tests;

public class UtilityTests
{
    [Fact]
    public void EasingCurve_evaluates_and_interpolates()
    {
        var root = TestShell.Run("""
            import Quickshell
            EasingCurve { curve.type: 0 }
            """, out var shell);
        using var _ = shell;
        Assert.Equal(0.25, shell.Eval(root, "valueAt(0.25)"));
        Assert.Equal(15.0, shell.Eval(root, "interpolate(0.5, 10, 20)"));
    }

    [Fact]
    public async Task ColorQuantizer_finds_the_colors_of_an_image()
    {
        using var shell = new TestShell(("shell.qml", """
            import Quickshell
            ColorQuantizer { source: "halves.png"; depth: 1 }
            """));
        // Left half red, right half blue.
        using (var bitmap = new SKBitmap(8, 8))
        {
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                    bitmap.SetPixel(x, y, x < 4 ? SKColors.Red : SKColors.Blue);
            }
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(shell.Directory, "halves.png"), data.ToArray());
        }
        var root = shell.Load();
        for (var i = 0; i < 100 && ((List<object?>)root.ReadProperty("colors")!).Count == 0; i++)
        {
            await Task.Delay(20);
            shell.Loop.Process();
        }
        var colors = ((List<object?>)root.ReadProperty("colors")!).Cast<QColor>().Select(c => c.ToString()).Order().ToList();
        Assert.Equal(["#0000ff", "#ff0000"], colors);
    }

    [Fact]
    public void BoundComponent_applies_its_properties_and_handlers()
    {
        using var shell = new TestShell(
            ("Clicker.qml", """
                import QtQuick
                Item {
                  required property string label
                  signal poked(int times)
                  implicitWidth: 40
                }
                """),
            ("shell.qml", """
                import QtQuick
                import Quickshell
                Item {
                  property alias bound: bound
                  property int pokes: 0
                  BoundComponent {
                    id: bound
                    source: "Clicker.qml"
                    property string label: "first"
                    function onPoked(times) { pokes += times }
                  }
                }
                """));
        var root = shell.Load();
        var bound = (QObject)root.ReadProperty("bound")!;
        var item = (QObject)bound.ReadProperty("item")!;
        Assert.Equal("first", item.ReadProperty("label"));
        Assert.Equal(40.0, bound.ReadProperty("implicitWidth"));
        bound.WriteProperty("label", "second");
        Assert.Equal("second", item.ReadProperty("label"));
        item.Emit("poked", 3);
        Assert.Equal(3, root.ReadProperty("pokes"));
    }
}
