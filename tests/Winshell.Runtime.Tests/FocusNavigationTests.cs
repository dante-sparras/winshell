using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class FocusNavigationTests
{
    private const int Tab = 0x01000001, Backtab = 0x01000002, Up = 0x01000013, Down = 0x01000015, Left = 0x01000012, Right = 0x01000014;
    private const int Return = 0x01000004, Escape = 0x01000000, Space = 0x20;

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

    private static bool Press(Scene scene, int key, int modifiers = 0)
    {
        var taken = scene.KeyPress(new KeyEvent { Key = key, Modifiers = modifiers });
        scene.KeyRelease(new KeyEvent { Key = key, Modifiers = modifiers });
        PolishQueue.Flush();
        return taken;
    }

    private static string? Focused(Scene scene) => scene.FocusItem?.ObjectName;

    [Fact]
    public void Tab_moves_through_the_tab_stops_in_order_and_wraps()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              Button { objectName: "one"; text: "One" }
              Text { text: "Not a stop" }
              Button { objectName: "off"; text: "Off"; enabled: false }
              Button { objectName: "hidden"; text: "Hidden"; visible: false }
              Rectangle { width: 10; height: 10; Slider { objectName: "two" } }
              TextField { objectName: "three" }
              Item { objectName: "four"; width: 10; height: 10; activeFocusOnTab: true }
              Control { objectName: "pane" }
            }
            """);
        using var _ = shell;
        var order = new List<string?>();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(Press(scene, Tab));
            order.Add(Focused(scene));
        }
        Assert.Equal(["one", "two", "three", "four", "one"], order);

        Press(scene, Tab, QtEnums.ShiftModifier);
        Assert.Equal("four", Focused(scene));
        Press(scene, Backtab, QtEnums.ShiftModifier);
        Assert.Equal("three", Focused(scene));
    }

    [Fact]
    public void Ctrl_tab_is_left_for_the_items()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Button { text: "One" }
            """);
        using var _ = shell;
        Assert.False(Press(scene, Tab, QtEnums.ControlModifier));
        Assert.Null(scene.FocusItem);
    }

    [Fact]
    public void A_radio_group_is_one_tab_stop_and_arrows_select_within_it()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              property string chosen: light.checked ? "light" : dark.checked ? "dark" : "system"
              Button { objectName: "before"; text: "Before" }
              Column {
                RadioButton { id: light; objectName: "light"; text: "Light" }
                RadioButton { id: dark; objectName: "dark"; text: "Dark"; checked: true }
                RadioButton { objectName: "system"; text: "System" }
              }
              Button { objectName: "after"; text: "After" }
            }
            """);
        using var _ = shell;
        Press(scene, Tab);
        Press(scene, Tab);
        Assert.Equal("dark", Focused(scene));
        Press(scene, Down);
        Assert.Equal("system", Focused(scene));
        Assert.Equal("system", root.ReadProperty("chosen"));
        Press(scene, Right);
        Assert.Equal("light", Focused(scene));
        Assert.Equal("light", root.ReadProperty("chosen"));
        Press(scene, Left);
        Assert.Equal("system", Focused(scene));
        Press(scene, Tab);
        Assert.Equal("after", Focused(scene));
        Press(scene, Backtab, QtEnums.ShiftModifier);
        Assert.Equal("system", Focused(scene));
    }

    [Fact]
    public void Arrows_move_through_a_list_view_and_select_as_they_go()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ListView {
              width: 200; height: 200
              model: 3
              delegate: ItemDelegate { objectName: "row" + index; text: "Row " + index; width: 200 }
            }
            """);
        using var _ = shell;
        Press(scene, Tab);
        Assert.Equal("row0", Focused(scene));
        Press(scene, Down);
        Press(scene, Down);
        Assert.Equal("row2", Focused(scene));
        Assert.Equal(2, root.ReadProperty("currentIndex"));
        Assert.False(Press(scene, Down));
        Assert.Equal("row2", Focused(scene));
        Press(scene, Tab);
        Assert.Equal("row2", Focused(scene));
    }

    [Fact]
    public void Enter_and_space_click_buttons_and_space_toggles_a_check_box()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              property int clicks: 0
              property alias on: box.checked
              Button { text: "Go"; onClicked: clicks++ }
              CheckBox { id: box; text: "On" }
            }
            """);
        using var _ = shell;
        Press(scene, Tab);
        Press(scene, Return);
        Press(scene, Space);
        Assert.Equal(2, root.ReadProperty("clicks"));
        Press(scene, Tab);
        Press(scene, Return);
        Assert.False((bool)root.ReadProperty("on")!);
        Press(scene, Space);
        Assert.True((bool)root.ReadProperty("on")!);
    }

    [Fact]
    public void A_menu_opened_from_the_keyboard_takes_focus_and_escape_gives_it_back()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias menuOpen: menu.visible
              Button { objectName: "opener"; text: "Open"; onClicked: menu.open() }
              Menu {
                id: menu
                MenuItem { objectName: "first"; text: "First" }
                MenuItem { objectName: "second"; text: "Second" }
              }
            }
            """);
        using var _ = shell;
        Press(scene, Tab);
        Press(scene, Return);
        Assert.True((bool)root.ReadProperty("menuOpen")!);
        Assert.Equal("first", Focused(scene));
        Press(scene, Down);
        Assert.Equal("second", Focused(scene));
        Press(scene, Down);
        Assert.Equal("first", Focused(scene));
        // Tab stays inside the open menu.
        Press(scene, Tab);
        Assert.Equal("first", Focused(scene));
        Press(scene, Escape);
        Assert.False((bool)root.ReadProperty("menuOpen")!);
        Assert.Equal("opener", Focused(scene));
    }

    [Fact]
    public void Escape_closes_a_popup_that_does_not_have_focus()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              property alias open: popup.visible
              Popup { id: popup; visible: true; width: 50; height: 50 }
            }
            """);
        using var _ = shell;
        Assert.True((bool)root.ReadProperty("open")!);
        Assert.True(Press(scene, Escape));
        Assert.False((bool)root.ReadProperty("open")!);
        Assert.False(Press(scene, Escape));
    }

    [Fact]
    public void A_modal_popup_keeps_tab_inside_it()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              Button { objectName: "under"; text: "Under" }
              Popup {
                modal: true; visible: true; width: 200; height: 100
                Column {
                  Button { objectName: "ok"; text: "OK" }
                  Button { objectName: "cancel"; text: "Cancel" }
                }
              }
            }
            """);
        using var _ = shell;
        var seen = new HashSet<string?>();
        for (var i = 0; i < 4; i++)
        {
            Press(scene, Tab);
            seen.Add(Focused(scene));
        }
        Assert.Equal(["cancel", "ok"], seen.Order());
    }

    [Fact]
    public void The_focus_rectangle_shows_after_the_keyboard_and_hides_after_the_pointer()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              property alias visual: button.visualFocus
              Button { id: button; text: "One"; width: 100; height: 32 }
              TextField { width: 100 }
            }
            """);
        using var _ = shell;
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(400, 400));
        Press(scene, Tab);
        Assert.True(scene.FocusVisible);
        Assert.True((bool)root.ReadProperty("visual")!);
        Assert.Same(scene.FocusItem, scene.Paint(surface.Canvas, TimeSpan.Zero).FocusRing);

        scene.PointerPress(300, 300, QtEnums.LeftButton);
        scene.PointerRelease(300, 300, QtEnums.LeftButton);
        Assert.False(scene.FocusVisible);
        Assert.False((bool)root.ReadProperty("visual")!);
        Assert.Null(scene.Paint(surface.Canvas, TimeSpan.Zero).FocusRing);

        // A text field shows focus with its own underline, not a rectangle.
        Press(scene, Tab);
        Press(scene, Tab);
        Assert.IsType<Quick.Controls.TextField>(scene.FocusItem);
        Assert.Null(scene.Paint(surface.Canvas, TimeSpan.Zero).FocusRing);
    }

    [Fact]
    public void Focus_policy_decides_whether_tab_stops_at_a_control()
    {
        var (_, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Column {
              Button { objectName: "skipped"; text: "A"; focusPolicy: Qt.NoFocus }
              Button { objectName: "stop"; text: "B" }
              Control { objectName: "pane"; focusPolicy: Qt.StrongFocus }
            }
            """);
        using var _ = shell;
        Press(scene, Tab);
        Assert.Equal("stop", Focused(scene));
        Press(scene, Tab);
        Assert.Equal("pane", Focused(scene));
    }

    private const string Stops = """
        import QtQuick
        Row {
          property Item one: a
          property Item three: c
          MouseArea { id: a; objectName: "one"; width: 40; height: 40; activeFocusOnTab: true }
          MouseArea { objectName: "two"; width: 40; height: 40; activeFocusOnTab: true }
          MouseArea { id: c; objectName: "three"; width: 40; height: 40; activeFocusOnTab: true }
          Item { objectName: "not a stop"; width: 40; height: 40 }
        }
        """;

    [Fact]
    public void The_next_item_in_the_focus_chain_follows_the_tab_order_both_ways_and_wraps()
    {
        var (root, scene, shell) = Show(Stops);
        using var _ = shell;
        var one = (Item)root.ReadProperty("one")!;
        var three = (Item)root.ReadProperty("three")!;
        Assert.Equal("two", one.NextItemInFocusChain()!.ObjectName);
        Assert.Equal("three", one.NextItemInFocusChain(false)!.ObjectName);
        Assert.Equal("one", three.NextItemInFocusChain()!.ObjectName);
        Assert.NotNull(scene);
    }

    [Fact]
    public void Focus_given_for_a_keyboard_reason_shows_the_focus_rectangle_and_any_other_does_not()
    {
        var (root, scene, shell) = Show(Stops);
        using var _ = shell;
        var one = (Item)root.ReadProperty("one")!;
        var three = (Item)root.ReadProperty("three")!;

        one.ForceActiveFocus();
        Assert.Equal("one", Focused(scene));
        Assert.False(scene.FocusVisible);

        three.ForceActiveFocus(QtEnums.ShortcutFocusReason);
        Assert.Equal("three", Focused(scene));
        Assert.True(scene.FocusVisible);

        // A press of the pointer hides it again, and a mouse reason does not bring it back.
        scene.PointerPress(1, 1, QtEnums.LeftButton);
        scene.PointerRelease(1, 1, QtEnums.LeftButton);
        one.ForceActiveFocus(QtEnums.MouseFocusReason);
        Assert.False(scene.FocusVisible);
        one.ForceActiveFocus(QtEnums.TabFocusReason);
        Assert.True(scene.FocusVisible);
    }

    [Fact]
    public void Scripts_can_name_the_focus_reasons()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            Item {
              property var reasons: [Qt.MouseFocusReason, Qt.TabFocusReason, Qt.BacktabFocusReason, Qt.ShortcutFocusReason, Qt.OtherFocusReason]
            }
            """);
        using var _ = shell;
        Assert.Equal([0.0, 1, 2, 5, 7], ((IEnumerable<object?>)root.ReadProperty("reasons")!).Select(Convert.ToDouble));
        Assert.NotNull(scene);
    }

    [Fact]
    public void A_focused_item_that_is_destroyed_leaves_its_window_with_no_focus_item()
    {
        var (root, scene, shell) = Show(Stops);
        using var _ = shell;
        var one = (Item)root.ReadProperty("one")!;
        one.ForceActiveFocus(QtEnums.TabFocusReason);
        Assert.Same(one, scene.FocusItem);
        one.Destroy();
        Assert.Null(scene.FocusItem);
        // Tab starts again from the first stop that is left.
        Press(scene, Tab);
        Assert.Equal("two", Focused(scene));
    }
}
