using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class RangeKeyboardTests
{
    private const int Home = 0x01000010, End = 0x01000011, Left = 0x01000012, Up = 0x01000013, Right = 0x01000014;
    private const int PageUp = 0x01000016, PageDown = 0x01000017;

    [Fact]
    public void Home_end_and_page_keys_move_a_slider()
    {
        var (slider, shell) = Load<Slider>("""
            import QtQuick
            import QtQuick.Controls
            Slider { from: 0; to: 100; stepSize: 5; value: 20 }
            """);
        using var _ = shell;
        Assert.True(slider.KeyPress(Key(PageUp)));
        Assert.Equal(70, slider.Value);
        Assert.True(slider.KeyPress(Key(PageDown)));
        Assert.Equal(20, slider.Value);
        Assert.True(slider.KeyPress(Key(End)));
        Assert.Equal(100, slider.Value);
        Assert.True(slider.KeyPress(Key(Home)));
        Assert.Equal(0, slider.Value);
        Assert.True(slider.KeyPress(Key(Right)));
        Assert.Equal(5, slider.Value);
        Assert.False(slider.KeyPress(Key(0x01000001)));
    }

    [Fact]
    public void A_dial_follows_a_new_range_and_takes_arrow_keys()
    {
        var (dial, shell) = Load<Dial>("""
            import QtQuick
            import QtQuick.Controls
            Dial { from: 0; to: 10; value: 8; stepSize: 1 }
            """);
        using var _ = shell;
        dial.To = 3;
        Assert.Equal(3, dial.Value);
        dial.To = 10;
        dial.Value = 2;
        dial.From = 6;
        Assert.Equal(6, dial.Value);
        Assert.True(dial.KeyPress(Key(Right)));
        Assert.Equal(7, dial.Value);
        Assert.True(dial.KeyPress(Key(End)));
        Assert.Equal(10, dial.Value);
        Assert.True(dial.KeyPress(Key(Home)));
        Assert.Equal(6, dial.Value);
    }

    [Fact]
    public void A_range_slider_and_a_dial_are_tab_stops_and_the_handles_take_keys()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Controls
            Column {
              Button { objectName: "before"; text: "Before" }
              Dial { objectName: "dial"; from: 0; to: 10; value: 1 }
              RangeSlider { objectName: "range"; from: 0; to: 100 }
              Button { objectName: "after"; text: "After" }
            }
            """));
        var root = (Item)shell.Load();
        var scene = new Scene();
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        using var _ = shell;
        var before = Named(root, "before");
        var dial = (Dial)Named(root, "dial");
        var range = (RangeSlider)Named(root, "range");
        Assert.Equal("dial", before.NextItemInFocusChain()!.ObjectName);
        Assert.Equal("range", dial.NextItemInFocusChain()!.ObjectName);
        Assert.Equal("after", range.NextItemInFocusChain()!.ObjectName);

        range.Second.Value = 40;
        range.First.Value = 20;
        Assert.True(range.KeyPress(Key(Right)));
        Assert.Equal((20.0, 50.0), (range.First.Value, range.Second.Value));
        Assert.True(range.KeyPress(Key(Up, QtEnums.ShiftModifier)));
        Assert.Equal(30, range.First.Value);
        Assert.True(range.KeyPress(Key(End)));
        Assert.Equal(100, range.Second.Value);
        Assert.True(range.KeyPress(Key(Home, QtEnums.ShiftModifier)));
        Assert.Equal(0, range.First.Value);
        Assert.True(dial.KeyPress(Key(Up)));
        Assert.Equal(2, dial.Value);
    }

    [Fact]
    public void A_horizontal_wheel_turn_does_not_change_a_spin_box_or_a_combo_box()
    {
        var shell = new TestShell(("shell.qml", """
            import QtQuick
            import QtQuick.Controls
            Item {
              SpinBox { objectName: "spin"; value: 5; wheelEnabled: true }
              ComboBox { objectName: "box"; model: ["a", "b", "c"]; currentIndex: 1; wheelEnabled: true }
            }
            """));
        var root = (Item)shell.Load();
        using var _ = shell;
        var spin = (SpinBox)Named(root, "spin");
        var box = (ComboBox)Named(root, "box");
        Assert.False(spin.Wheel(0, 0, 120, 0, 0, 0, 0, 0, false));
        Assert.Equal(5, spin.Value);
        Assert.False(box.Wheel(0, 0, 120, 0, 0, 0, 0, 0, false));
        Assert.Equal(1, box.CurrentIndex);
        Assert.True(spin.Wheel(0, 0, 0, 120, 0, 0, 0, 0, false));
        Assert.Equal(6, spin.Value);
        Assert.True(box.Wheel(0, 0, 0, -120, 0, 0, 0, 0, false));
        Assert.Equal(2, box.CurrentIndex);
    }

    private static KeyEvent Key(int key, int modifiers = 0) => new() { Key = key, Modifiers = modifiers };

    private static (T Control, TestShell Shell) Load<T>(string qml) where T : Item
    {
        var shell = new TestShell(("shell.qml", qml));
        return ((T)shell.Load(), shell);
    }

    private static Item Named(Item root, string objectName) =>
        root.ObjectName == objectName ? root : root.ChildItems.Select(child => Try(child, objectName)).First(item => item is not null)!;

    private static Item? Try(Item item, string objectName)
    {
        if (item.ObjectName == objectName)
            return item;
        foreach (var child in item.ChildItems)
        {
            var found = Try(child, objectName);
            if (found is not null)
                return found;
        }
        return null;
    }
}
