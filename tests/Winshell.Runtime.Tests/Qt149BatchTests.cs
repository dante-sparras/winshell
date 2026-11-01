using System.Globalization;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class Qt149BatchTests
{
    [Fact]
    public void Time_held_counts_the_press_and_clears_on_release()
    {
        var (root, scene, shell) = Show("import QtQuick\nItem { width: 80; height: 80; TapHandler {} }");
        using var _ = shell;
        scene.PointerPress(10, 10, QtEnums.LeftButton);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(400));
        var tap = (TapHandler)root.PointerHandlers[0];
        Assert.Equal(0.4, tap.TimeHeld, 3);
        scene.PointerRelease(10, 10, QtEnums.LeftButton);
        Assert.Equal(0, tap.TimeHeld);
    }

    [Fact]
    public void Drag_threshold_cancels_a_tap_and_release_within_bounds_does_not()
    {
        var (root, scene, shell) = Show("import QtQuick\nItem { width: 400; height: 80; Item { objectName: \"drag\"; width: 180; height: 80; property int taps: 0; TapHandler { onTapped: parent.taps++ } } Item { objectName: \"rel\"; x: 200; width: 180; height: 80; property int taps: 0; TapHandler { gesturePolicy: 2; onTapped: parent.taps++ } } }");
        using var _ = shell;
        Click(scene, 20, 120);
        Click(scene, 220, 320);
        Assert.Equal(0, Taps(root, "drag"));
        Assert.Equal(1, Taps(root, "rel"));
    }

    [Fact]
    public void Drag_within_bounds_keeps_a_tap_until_the_pointer_leaves()
    {
        var (root, scene, shell) = Show("import QtQuick\nItem { width: 400; height: 80; Item { objectName: \"drag\"; width: 180; height: 80; property int taps: 0; TapHandler { gesturePolicy: 3; onTapped: parent.taps++ } } }");
        using var _ = shell;
        Click(scene, 20, 120);
        Assert.Equal(1, Taps(root, "drag"));

        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerMove(300, 20);
        scene.PointerMove(20, 20);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(1, Taps(root, "drag"));
    }

    [Fact]
    public void A_handler_with_no_accepted_modifiers_ignores_the_modifier_keys()
    {
        var (root, scene, shell) = Show("import QtQuick\nItem { id: box; width: 80; height: 80; property int taps: 0; TapHandler { onTapped: box.taps++ } }");
        using var _ = shell;
        scene.PointerPress(20, 20, QtEnums.LeftButton, QtEnums.ShiftModifier);
        scene.PointerRelease(20, 20, QtEnums.LeftButton, QtEnums.ShiftModifier);
        Assert.Equal(1, Convert.ToInt32(root.ReadProperty("taps"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Accepted_modifiers_must_match_the_event()
    {
        var (root, scene, shell) = Show("import QtQuick\nItem { id: box; width: 80; height: 80; property int taps: 0; property bool hot: false; HoverHandler { acceptedModifiers: Qt.ShiftModifier; onHoveredChanged: box.hot = hovered } TapHandler { acceptedModifiers: Qt.ShiftModifier; onTapped: box.taps++ } }");
        using var _ = shell;
        scene.PointerMove(20, 20);
        Assert.False(root.ReadProperty("hot") is true);
        scene.PointerMove(20, 20, QtEnums.ShiftModifier);
        Assert.True(root.ReadProperty("hot") is true);
        scene.PointerPress(20, 20, QtEnums.LeftButton);
        scene.PointerRelease(20, 20, QtEnums.LeftButton);
        Assert.Equal(0, Convert.ToInt32(root.ReadProperty("taps"), CultureInfo.InvariantCulture));
        scene.PointerPress(20, 20, QtEnums.LeftButton, QtEnums.ShiftModifier);
        scene.PointerRelease(20, 20, QtEnums.LeftButton, QtEnums.ShiftModifier);
        Assert.Equal(1, Convert.ToInt32(root.ReadProperty("taps"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Translation_is_the_movement_since_the_press()
    {
        var (root, scene, shell) = Show("import QtQuick\nRectangle { x: 10; y: 20; width: 40; height: 40; DragHandler {} }");
        using var _ = shell;
        scene.PointerPress(30, 40, QtEnums.LeftButton);
        scene.PointerMove(130, 90);
        Assert.Equal(new QPoint(100, 50), ((DragHandler)root.PointerHandlers[0]).Translation);
        Assert.Equal(110, root.X);
        Assert.Equal(70, root.Y);
    }

    [Fact]
    public void An_extended_state_applies_the_base_then_overrides_it()
    {
        using var shell = Load("import QtQuick\nItem { id: box; property int m: 1; property int n: 1; states: [ State { name: \"base\"; PropertyChanges { target: box; m: 5; n: 5 } }, State { name: \"more\"; extend: \"base\"; PropertyChanges { target: box; n: 9 } } ] }", out var root);
        ((Item)root).State = "more";
        Assert.Equal(5, Convert.ToInt32(root.ReadProperty("m"), CultureInfo.InvariantCulture));
        Assert.Equal(9, Convert.ToInt32(root.ReadProperty("n"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Explicit_changes_copy_the_value_once()
    {
        using var shell = Load("import QtQuick\nItem { id: root; property int source: 3; property int snap: 0; states: State { name: \"on\"; PropertyChanges { target: root; snap: root.source; explicit: true } } }", out var root);
        ((Item)root).State = "on";
        Assert.Equal(3, Convert.ToInt32(root.ReadProperty("snap"), CultureInfo.InvariantCulture));
        root.WriteProperty("source", 8);
        Assert.Equal(3, Convert.ToInt32(root.ReadProperty("snap"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Always_run_to_end_finishes_after_running_is_cleared()
    {
        using var shell = Load("import QtQuick\nItem { id: root; property bool go: true; property real w: 0; NumberAnimation { target: root; property: \"w\"; from: 0; to: 100; duration: 1000; alwaysRunToEnd: true; running: root.go } }", out var root);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        Assert.InRange(Convert.ToDouble(root.ReadProperty("w"), CultureInfo.InvariantCulture), 1, 99);
        root.WriteProperty("go", false);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.Equal(100, Convert.ToDouble(root.ReadProperty("w"), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Always_run_to_end_finishes_the_loop_it_is_on()
    {
        using var shell = Load("import QtQuick\nItem { id: root; property bool go: true; property real w: 0; property bool live: spin.running; NumberAnimation { id: spin; target: root; property: \"w\"; from: 0; to: 100; duration: 1000; loops: Animation.Infinite; alwaysRunToEnd: true; running: root.go } }", out var root);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        root.WriteProperty("go", false);
        Assert.True(root.ReadProperty("live") is true);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.Equal(100, Convert.ToDouble(root.ReadProperty("w"), CultureInfo.InvariantCulture));
        Assert.False(root.ReadProperty("live") is true);
    }

    [Fact]
    public void A_delayed_binding_waits_until_the_next_turn()
    {
        using var shell = Load("import QtQuick\nItem { id: root; property int source: 1; property int seen: 0; Binding { target: root; property: \"seen\"; value: root.source; delayed: true } }", out var root);
        Assert.Equal(1, Convert.ToInt32(root.ReadProperty("seen"), CultureInfo.InvariantCulture));
        root.WriteProperty("source", 7);
        Assert.Equal(1, Convert.ToInt32(root.ReadProperty("seen"), CultureInfo.InvariantCulture));
        shell.Loop.Process();
        Assert.Equal(7, Convert.ToInt32(root.ReadProperty("seen"), CultureInfo.InvariantCulture));
    }

    private static TestShell Load(string qml, out QObject root)
    {
        root = TestShell.Run(qml, out var shell);
        return shell;
    }

    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(400);
        scene.Root.SetLayoutHeight(400);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static void Click(Scene scene, double x, double moved)
    {
        scene.PointerPress(x, 20, QtEnums.LeftButton);
        scene.PointerMove(moved, 20);
        scene.PointerRelease(moved, 20, QtEnums.LeftButton);
    }

    private static int Taps(Item item, string name)
    {
        var found = item.ObjectName == name ? item : item.ChildItems.First(child => child.ObjectName == name);
        return Convert.ToInt32(found.ReadProperty("taps"), CultureInfo.InvariantCulture);
    }
}
