using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Winshell.Runtime.Tests;

public class UtilityTests
{
    [Fact]
    public void EasingCurve_evaluates_and_interpolates()
    {
        var root = TestShell.Run("""
            import Winshell
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
            import Winshell
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
        await Wait.UntilAsync(() => ((List<object?>)root.ReadProperty("colors")!).Count > 0, "the image colors", shell.Loop.Process);
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
                import Winshell
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

    [Fact]
    public void BoundComponent_sourceComponent_loads_an_inline_component()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import Winshell
            Item {
              id: root
              property int pokes: 0
              property alias bound: bound
              BoundComponent {
                id: bound
                sourceComponent: Item {
                  required property string label
                  signal poked(int times)
                  implicitWidth: 40
                  Component.onCompleted: bound.label = "during"
                }
                property string label: "first"
                function onPoked(times) { root.pokes += times }
              }
            }
            """));
        var root = shell.Load();
        var bound = (QObject)root.ReadProperty("bound")!;
        var item = (QObject)bound.ReadProperty("item")!;
        // The child's completion ran before the values were pushed again, so the loaded object sees it.
        Assert.Equal("during", item.ReadProperty("label"));
        Assert.Equal(40.0, bound.ReadProperty("implicitWidth"));
        Assert.Null(bound.ReadProperty("sourceComponent"));
        bound.WriteProperty("label", "second");
        Assert.Equal("second", item.ReadProperty("label"));
        item.Emit("poked", 3);
        Assert.Equal(3, root.ReadProperty("pokes"));
        Assert.DoesNotContain(shell.Messages, IsProblem);
    }

    [Fact]
    public void BoundComponent_bindValues_false_sets_properties_once()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import Winshell
            Item {
              id: root
              property int pokes: 0
              property alias bound: bound
              BoundComponent {
                id: bound
                bindValues: false
                sourceComponent: Item {
                  required property string label
                  signal poked(int times)
                  Component.onCompleted: bound.label = "during"
                }
                property string label: "first"
                function onPoked(times) { root.pokes += times }
              }
            }
            """));
        var root = shell.Load();
        var bound = (QObject)root.ReadProperty("bound")!;
        var item = (QObject)bound.ReadProperty("item")!;
        Assert.Equal("first", item.ReadProperty("label"));
        Assert.Equal("during", bound.ReadProperty("label"));
        bound.WriteProperty("label", "second");
        Assert.Equal("second", bound.ReadProperty("label"));
        Assert.Equal("first", item.ReadProperty("label"));
        item.Emit("poked", 3);
        Assert.Equal(3, root.ReadProperty("pokes"));
        Assert.DoesNotContain(shell.Messages, IsProblem);
    }

    [Fact]
    public void BoundComponent_loads_whichever_source_was_set_last()
    {
        using var shell = new TestShell(
            ("Card.qml", """
                import QtQuick
                Item { objectName: "file" }
                """),
            ("shell.qml", """
                import QtQuick
                import Winshell
                Item {
                  Component { id: card; Item { objectName: "kept" } }
                  property Component held: card
                  property alias componentWins: componentWins
                  property alias fileWins: fileWins
                  property alias missingKeeps: missingKeeps
                  property alias keepsCaller: keepsCaller
                  property alias empty: empty

                  // Object assignments run in source order. An id binding would wait until every literal is done.
                  BoundComponent {
                    id: componentWins
                    source: "Card.qml"
                    sourceComponent: Item { objectName: "inline" }
                  }
                  BoundComponent {
                    id: fileWins
                    sourceComponent: Item { objectName: "inline" }
                    source: "Card.qml"
                  }
                  BoundComponent {
                    id: missingKeeps
                    sourceComponent: Item { objectName: "inline" }
                    source: "missing.qml"
                  }
                  BoundComponent { id: keepsCaller; sourceComponent: card }
                  BoundComponent { id: empty }
                }
                """));
        var root = shell.Load();
        Assert.Equal("inline", LoadedName(root, "componentWins"));
        Assert.Equal("Card.qml", ((QObject)root.ReadProperty("componentWins")!).ReadProperty("source"));
        Assert.Equal("file", LoadedName(root, "fileWins"));
        Assert.Equal("inline", LoadedName(root, "missingKeeps"));
        Assert.Equal("", ((QObject)root.ReadProperty("missingKeeps")!).ReadProperty("source"));
        Assert.Equal("kept", LoadedName(root, "keepsCaller"));
        Assert.Null(((QObject)root.ReadProperty("empty")!).ReadProperty("item"));
        Assert.Null(((QObject)root.ReadProperty("componentWins")!).ReadProperty("sourceComponent"));
        Assert.Null(((QObject)root.ReadProperty("keepsCaller")!).ReadProperty("sourceComponent"));
        var held = (QObject)root.ReadProperty("held")!;
        Assert.False(held.IsDestroyed);
        Assert.Contains(shell.Messages, m => m.Contains("BoundComponent: Cannot open: missing.qml", StringComparison.Ordinal));
        Assert.Contains(shell.Messages, m => m.Contains("BoundComponent has no component", StringComparison.Ordinal));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("Cannot open: Card.qml", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundComponent_ignores_changes_after_load()
    {
        using var shell = new TestShell(
            ("A.qml", """
                import QtQuick
                Item {
                  objectName: "from-file"
                  Component.onCompleted: parent.source = "Other.qml"
                }
                """),
            ("Other.qml", """
                import QtQuick
                Item { objectName: "other-file" }
                """),
            ("shell.qml", """
                import QtQuick
                import Winshell
                Item {
                  property alias bound: bound
                  property Component other: Component { Item { objectName: "other" } }
                  BoundComponent {
                    id: bound
                    source: "A.qml"
                  }
                }
                """));
        var root = shell.Load();
        var bound = (QObject)root.ReadProperty("bound")!;
        var item = (QObject)bound.ReadProperty("item")!;
        Assert.Equal("from-file", item.ReadProperty("objectName"));
        Assert.Equal("A.qml", bound.ReadProperty("source"));
        Assert.Contains(shell.Messages, m => m.Contains("BoundComponent: source cannot be set after creation", StringComparison.Ordinal));

        var count = shell.Messages.Count;
        bound.WriteProperty("source", "A.qml");
        Assert.Equal(count, shell.Messages.Count);

        bound.WriteProperty("source", "Other.qml");
        Assert.Same(item, bound.ReadProperty("item"));
        Assert.Equal("A.qml", bound.ReadProperty("source"));
        Assert.Contains(shell.Messages, m => m.Contains("BoundComponent: source cannot be set after creation", StringComparison.Ordinal));

        count = shell.Messages.Count;
        bound.WriteProperty("sourceComponent", null);
        Assert.Equal(count, shell.Messages.Count);

        bound.WriteProperty("sourceComponent", root.ReadProperty("other"));
        Assert.Same(item, bound.ReadProperty("item"));
        Assert.Contains(shell.Messages, m => m.Contains("BoundComponent: sourceComponent cannot be set after creation", StringComparison.Ordinal));

        bound.WriteProperty("bindValues", true);
        Assert.Equal(true, bound.ReadProperty("bindValues"));
        Assert.Contains(shell.Messages, m => m.Contains("BoundComponent: bindValues cannot be set after creation", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundComponent_releases_the_loaded_object()
    {
        using var shell = new TestShell(("shell.qml", """
            import QtQuick
            import Winshell
            Item {
              property alias bound: bound
              BoundComponent {
                id: bound
                sourceComponent: Item { property string label }
                property string label: "kept"
              }
            }
            """));
        var weak = TrackLoadedItem(shell);
        Collect();
        Assert.False(weak.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TrackLoadedItem(TestShell shell)
    {
        var root = shell.Load();
        var bound = (QObject)root.ReadProperty("bound")!;
        var item = (QObject)bound.ReadProperty("item")!;
        var weak = new WeakReference(item);
        root.Destroy();
        return weak;
    }

    private static void Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static string LoadedName(QObject root, string name)
    {
        var bound = (QObject)root.ReadProperty(name)!;
        var item = (QObject)bound.ReadProperty("item")!;
        return (string)item.ReadProperty("objectName")!;
    }

    private static bool IsProblem(string message) =>
        message.StartsWith("Warn ", StringComparison.Ordinal) || message.StartsWith("Error ", StringComparison.Ordinal);
}
