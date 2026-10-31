using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

public class QtControlGapTests
{
    [Fact]
    public void A_modal_popup_closes_on_release_outside_its_parent_only()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              id: root; width: 300; height: 300; property var flyout: pop
              Item { id: box; x: 100; y: 100; width: 80; height: 80 }
              Popup { id: pop; modal: true; parent: box; x: 20; y: 20; width: 40; height: 40
                closePolicy: Popup.CloseOnReleaseOutsideParent }
            }
            """);
        using var _ = shell;
        var popup = (Popup)root.ReadProperty("flyout")!;
        popup.Open();
        shell.Loop.Process();
        scene.PointerPress(10, 10, QtEnums.LeftButton);
        scene.PointerRelease(130, 130, QtEnums.LeftButton);
        Assert.True(popup.Opened);
        scene.PointerPress(110, 110, QtEnums.LeftButton);
        scene.PointerRelease(110, 110, QtEnums.LeftButton);
        Assert.True(popup.Opened);
        scene.PointerPress(10, 10, QtEnums.LeftButton);
        scene.PointerRelease(10, 10, QtEnums.LeftButton);
        Assert.False(popup.Opened);
    }

    [Fact]
    public void Next_check_state_is_called_on_a_click()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            CheckBox { objectName: "box"; tristate: true; checkState: 0
              nextCheckState: function() { return checkState === 0 ? 2 : 0 } }
            """);
        using var _ = shell;
        var box = (CheckBox)Find(root, "box");
        box.Click();
        Assert.Equal(2, box.CheckState);
        box.Click();
        Assert.Equal(0, box.CheckState);
    }

    [Fact]
    public void A_scroll_indicator_draws_only_while_active()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ScrollIndicator { objectName: "bar"; width: 12; height: 40; size: 0.5; position: 0 }
            """, 12, 40);
        using var _ = shell;
        using var hidden = Render(scene, 12, 40);
        Assert.Equal(0, Ink(hidden));
        ((ScrollIndicator)Find(root, "bar")).Active = true;
        shell.Loop.Process();
        using var shown = Render(scene, 12, 40);
        Assert.True(Ink(shown) > 0);
    }

    [Fact]
    public void Snap_on_release_waits_until_the_pointer_lets_go()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              RangeSlider { objectName: "range"; width: 200; height: 40; from: 0; to: 1; stepSize: 0.25; snapMode: 2 }
              Dial { objectName: "dial"; y: 50; width: 100; height: 100; from: 0; to: 1; stepSize: 0.5; snapMode: 2 }
            }
            """);
        using var _ = shell;
        var range = (RangeSlider)Find(root, "range");
        range.PointerPress(64, 20, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        Assert.InRange(range.First.Value, 0.2, 0.4);
        range.PointerRelease(64, 20, QtEnums.LeftButton, 0, 0);
        Assert.Equal(0.25, range.First.Value);
        var dial = (Dial)Find(root, "dial");
        var angle = -84 * Math.PI / 180;
        var x = 50 + Math.Sin(angle) * 30;
        var y = 50 - Math.Cos(angle) * 30;
        dial.PointerPress(x, y, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        Assert.InRange(dial.Value, 0.1, 0.4);
        dial.PointerRelease(x, y, QtEnums.LeftButton, 0, 0);
        Assert.Equal(0, dial.Value);
    }

    [Fact]
    public void Menu_current_index_follows_keys_and_hover()
    {
        var (root, scene, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            Item {
              id: root; width: 300; height: 300; property var menu: menu
              Menu { id: menu; MenuItem { text: "One" } MenuItem { text: "Two" } }
            }
            """);
        using var _ = shell;
        var menu = (Menu)root.ReadProperty("menu")!;
        menu.Popup(20.0, 20.0);
        shell.Loop.Process();
        PolishQueue.Flush();
        Assert.Equal(-1, menu.CurrentIndex);
        scene.KeyPress(new KeyEvent { Key = 0x01000015 });
        Assert.Equal(0, menu.CurrentIndex);
        var second = (MenuItem)menu.ItemAt(1)!;
        var at = (QPoint)second.MapToItem(scene.Root, second.Width / 2, second.Height / 2);
        scene.PointerMove(at.X, at.Y);
        Assert.Equal(1, menu.CurrentIndex);
        menu.Close();
        Assert.Equal(-1, menu.CurrentIndex);
        Assert.True(second.Highlighted);
    }

    private static int Ink(SKBitmap bitmap)
    {
        var n = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha > 0)
                    n++;
        return n;
    }

    private static SKBitmap Render(Scene scene, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        scene.Paint(canvas, TimeSpan.Zero);
        return bitmap;
    }

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

    private static Item Find(Item root, string objectName)
    {
        if (root.ObjectName == objectName)
            return root;
        foreach (var child in root.ChildItems)
        {
            try { return Find(child, objectName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"No item named {objectName}");
    }
}
