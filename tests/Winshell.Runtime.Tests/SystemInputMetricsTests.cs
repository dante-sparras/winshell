using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Drag thresholds and the double-tap window come from the Windows mouse settings in the theme.</summary>
[Collection(ThemeCollection.Name)]
public class SystemInputMetricsTests
{
    [Fact]
    public void A_mouse_area_drag_threshold_defaults_to_the_theme()
    {
        var before = Theme.Current;
        try
        {
            Theme.Current = before with { DragThreshold = 7 };
            using var shell = new TestShell(("shell.qml", """
                import QtQuick
                MouseArea { }
                """));
            var area = (MouseArea)shell.Load();

            Assert.Equal(7, area.Drag.Threshold);
            Assert.Equal(4, Theme.Default(false).DragThreshold);
        }
        finally
        {
            Theme.Current = before;
        }
    }

    [Fact]
    public void A_second_tap_after_the_double_click_time_is_not_a_double_tap()
    {
        var before = Theme.Current;
        try
        {
            Theme.Current = before with { DoubleClickTime = 200 };
            var (shell, scene, root) = Load();
            using var _ = shell;

            Tap(scene, 20, 20);
            shell.Loop.Advance(TimeSpan.FromMilliseconds(250));
            Tap(scene, 20, 20);
            Assert.Equal(0, Convert.ToInt32(root.ReadProperty("doubles")));

            shell.Loop.Advance(TimeSpan.FromMilliseconds(150));
            Tap(scene, 20, 20);
            Assert.Equal(1, Convert.ToInt32(root.ReadProperty("doubles")));
        }
        finally
        {
            Theme.Current = before;
        }
    }

    [Fact]
    public void A_second_tap_outside_the_double_click_rectangle_is_not_a_double_tap()
    {
        var before = Theme.Current;
        try
        {
            Theme.Current = before with { DoubleClickWidth = 10, DoubleClickHeight = 6 };
            var (shell, scene, root) = Load();
            using var _ = shell;

            Tap(scene, 20, 20);
            Tap(scene, 26, 20);
            Assert.Equal(0, Convert.ToInt32(root.ReadProperty("doubles")));

            Tap(scene, 30, 24);
            Assert.Equal(0, Convert.ToInt32(root.ReadProperty("doubles")));

            Tap(scene, 34, 22);
            Assert.Equal(1, Convert.ToInt32(root.ReadProperty("doubles")));
        }
        finally
        {
            Theme.Current = before;
        }
    }

    private static void Tap(Scene scene, double x, double y)
    {
        scene.PointerPress(x, y, QtEnums.LeftButton);
        scene.PointerRelease(x, y, QtEnums.LeftButton);
    }

    private static (TestShell Shell, Scene Scene, Item Root) Load()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            Item {
              width: 100
              height: 100
              property int doubles: 0
              TapHandler { onDoubleTapped: { doubles++ } }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(100);
        scene.Root.SetLayoutHeight(100);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (shell, scene, root);
    }
}
