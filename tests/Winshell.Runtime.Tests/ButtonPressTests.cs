using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class ButtonPressTests
{
    [Fact]
    public void Losing_focus_cancels_a_space_press()
    {
        var scene = new Scene();
        var button = new Button { Width = 40, Height = 24 };
        var other = new Button { Width = 40, Height = 24 };
        button.SetParentItem(scene.Root);
        other.SetParentItem(scene.Root);
        button.ForceActiveFocus();
        var signals = new List<string>();
        button.Connect("canceled", _ => signals.Add("canceled"));
        button.Connect("clicked", _ => signals.Add("clicked"));

        Assert.True(button.KeyPress(new KeyEvent { Key = 0x20 }));
        Assert.True(button.IsDown);
        other.ForceActiveFocus();

        Assert.False(button.IsDown);
        Assert.Equal(["canceled"], signals);
    }

    [Fact]
    public void A_finished_switch_drag_releases_without_canceling()
    {
        var toggle = new Switch { Width = 40, Height = 20 };
        var signals = new List<string>();
        toggle.Connect("canceled", _ => signals.Add("canceled"));
        toggle.Connect("released", _ => signals.Add("released"));
        toggle.Connect("clicked", _ => signals.Add("clicked"));

        Assert.True(toggle.PointerPress(0, 10, QtEnums.LeftButton, QtEnums.LeftButton, 0));
        toggle.PointerMove(16, 10, QtEnums.LeftButton, 0);
        Assert.True(toggle.Position > 0.5);
        toggle.PointerRelease(16, 10, QtEnums.LeftButton, 0, 0);

        Assert.Equal(["released", "clicked"], signals);
        Assert.True(toggle.Checked);
        Assert.Equal(1, toggle.Position);
    }

    [Fact]
    public void Canceling_a_switch_drag_lets_the_knob_spring_back()
    {
        var toggle = new Switch { Width = 40, Height = 20 };
        var signals = new List<string>();
        toggle.Connect("canceled", _ => signals.Add("canceled"));
        toggle.Connect("released", _ => signals.Add("released"));

        Assert.True(toggle.PointerPress(0, 10, QtEnums.LeftButton, QtEnums.LeftButton, 0));
        toggle.PointerMove(16, 10, QtEnums.LeftButton, 0);
        Assert.True(toggle.Position > 0.5);
        toggle.PointerCancel();

        Assert.Equal(["canceled"], signals);
        Assert.False(toggle.Checked);
        Assert.Equal(0, toggle.Position);
    }
}
