using System.Globalization;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class ControlsTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml, double width = 300, double height = 300)
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

    [Fact]
    public void Controls_effects_shapes_and_canvas_import_and_create()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            import QtQuick.Effects
            import QtQuick.Shapes
            import Qt5Compat.GraphicalEffects as GE
            Column {
              property int made: children.length
              Button { text: "B" }
              Slider { value: 0.5 }
              TextField {}
              MultiEffect {}
              GE.DropShadow {}
              Shape { ShapePath { startX: 0; startY: 0; PathLine { x: 10; y: 10 } } }
              Canvas {}
            }
            """);
        using var _ = shell;
        Assert.Equal(7, root.ReadProperty("made"));
    }

    [Fact]
    public void CheckBox_toggles_when_clicked()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias on: box.checked
              CheckBox { id: box; text: "Check" }
            }
            """);
        using var _ = shell;
        scene.PointerPress(10, 10, QtEnums.LeftButton);
        scene.PointerRelease(10, 10, QtEnums.LeftButton);
        Assert.True((bool)root.ReadProperty("on")!);
    }

    [Fact]
    public void ComboBox_popup_is_as_tall_as_its_rows_when_it_opens()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias box: box
              ComboBox { id: box; y: 100; model: ["First", "Second", "Third"] }
            }
            """);
        using var _ = shell;
        scene.PointerPress(20, 110, QtEnums.LeftButton);
        scene.PointerRelease(20, 110, QtEnums.LeftButton);
        shell.Loop.Process();
        var popup = (QObject)((QObject)root.ReadProperty("box")!).ReadProperty("popup")!;
        Assert.True((bool)popup.ReadProperty("visible")!);
        Assert.True(Convert.ToDouble(popup.ReadProperty("height"), CultureInfo.InvariantCulture) >= 3 * 32);
    }
}
