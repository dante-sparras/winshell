using SkiaSharp;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Runtime.Tests;

/// <summary>The scroll bar's <c>active</c>, the Windows "Always show scrollbars" setting, and its held buttons.</summary>
[Collection(ThemeCollection.Name)]
public class ScrollBarWindowsBehaviourTests
{
    private static (Item Root, Scene Scene, TestShell Shell) Show(string qml)
    {
        var shell = new TestShell(("shell.qml", qml));
        var root = (Item)shell.Load();
        var scene = new Scene();
        scene.Root.SetLayoutWidth(200);
        scene.Root.SetLayoutHeight(200);
        root.SetParentItem(scene.Root);
        shell.Loop.Process();
        PolishQueue.Flush();
        return (root, scene, shell);
    }

    private static int ThumbWidth(Scene scene, TimeSpan time, int width, int y = 60)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        scene.Paint(canvas, time);
        return Enumerable.Range(0, width).Count(x => bitmap.GetPixel(x, y).Red > 80);
    }

    private const string Bar = """
        import QtQuick
        import QtQuick.Controls
        ScrollBar { height: 160; orientation: Qt.Vertical; size: 0.5; policy: ScrollBar.AlwaysOn }
        """;

    private static void WithTheme(Theme theme, Action test)
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = theme;
            test();
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void An_active_scroll_bar_widens_at_once_and_stays_wide_while_active()
    {
        WithTheme(Theme.Default(true), () =>
        {
            var (root, scene, shell) = Show(Bar);
            using var _ = shell;
            var bar = (Quick.Controls.ScrollBar)root;
            var start = TimeSpan.FromSeconds(10);
            Assert.Equal(2, ThumbWidth(scene, start, 12));
            bar.Active = true;
            ThumbWidth(scene, start + TimeSpan.FromMilliseconds(16), 12);
            Assert.Equal(6, ThumbWidth(scene, start + TimeSpan.FromMilliseconds(200), 12));
            Assert.Equal(6, ThumbWidth(scene, start + TimeSpan.FromSeconds(5), 12));
            bar.Active = false;
            ThumbWidth(scene, start + TimeSpan.FromSeconds(5.1), 12);
            Assert.Equal(6, ThumbWidth(scene, start + TimeSpan.FromSeconds(6), 12));
            ThumbWidth(scene, start + TimeSpan.FromSeconds(7.2), 12);
            Assert.Equal(2, ThumbWidth(scene, start + TimeSpan.FromSeconds(7.5), 12));
        });
    }

    [Fact]
    public void Active_follows_hover_only_while_the_bar_is_interactive()
    {
        var (root, _, shell) = Show(Bar);
        using var _ = shell;
        var bar = (Quick.Controls.ScrollBar)root;
        bar.HoverEnter(6, 60, 0);
        Assert.True(bar.Active);
        bar.Interactive = false;
        Assert.False(bar.Active);
    }

    [Fact]
    public void With_always_show_scrollbars_the_bar_is_wide_without_the_pointer()
    {
        WithTheme(Theme.Default(true) with { AutoHideScrollBars = false }, () =>
        {
            var (_, scene, shell) = Show(Bar);
            using var _ = shell;
            var start = TimeSpan.FromSeconds(10);
            Assert.Equal(6, ThumbWidth(scene, start, 12));
            Assert.Equal(6, ThumbWidth(scene, start + TimeSpan.FromSeconds(5), 12));
        });
    }

    [Fact]
    public void Turning_on_always_show_scrollbars_widens_a_resting_bar()
    {
        WithTheme(Theme.Default(true), () =>
        {
            var (_, scene, shell) = Show(Bar);
            using var _ = shell;
            var start = TimeSpan.FromSeconds(10);
            Assert.Equal(2, ThumbWidth(scene, start, 12));
            Theme.Current = Theme.Default(true) with { AutoHideScrollBars = false };
            ThumbWidth(scene, start + TimeSpan.FromMilliseconds(16), 12);
            Assert.Equal(6, ThumbWidth(scene, start + TimeSpan.FromMilliseconds(200), 12));
        });
    }

    [Fact]
    public void The_thumb_widens_on_hover_under_a_custom_background()
    {
        WithTheme(Theme.Default(true), () =>
        {
            var (root, scene, shell) = Show("""
                import QtQuick
                import QtQuick.Controls
                ScrollBar {
                  height: 160; orientation: Qt.Vertical; size: 0.5; policy: ScrollBar.AlwaysOn
                  background: Item { implicitWidth: 12 }
                }
                """);
            using var _ = shell;
            var bar = (Quick.Controls.ScrollBar)root;
            var start = TimeSpan.FromSeconds(10);
            Assert.Equal(2, ThumbWidth(scene, start, 12));
            bar.HoverEnter(6, 60, 0);
            ThumbWidth(scene, start + TimeSpan.FromMilliseconds(16), 12);
            ThumbWidth(scene, start + TimeSpan.FromMilliseconds(420), 12);
            Assert.Equal(6, ThumbWidth(scene, start + TimeSpan.FromMilliseconds(600), 12));
        });
    }

    [Fact]
    public void Pressing_the_track_pages_toward_the_pointer_until_the_thumb_reaches_it()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ScrollBar { height: 160; orientation: Qt.Vertical; size: 0.25; position: 0; policy: ScrollBar.AlwaysOn }
            """);
        using var _ = shell;
        var bar = (Quick.Controls.ScrollBar)root;
        // The track is the 128 pixels between the arrows, and the thumb a quarter of it: 16..48 at first.
        bar.PointerPress(6, 16 + 70, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        Assert.Equal(0.25, bar.Position, 3);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(450));
        Assert.Equal(0.5, bar.Position, 3);
        // The thumb now covers the pointer, so holding longer pages no further.
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(0.5, bar.Position, 3);
        bar.PointerRelease(6, 16 + 70, QtEnums.LeftButton, 0, 0);

        bar.PointerPress(6, 16 + 4, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        Assert.Equal(0.25, bar.Position, 3);
        bar.PointerRelease(6, 16 + 4, QtEnums.LeftButton, 0, 0);
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0.25, bar.Position, 3);
    }

    [Fact]
    public void Holding_an_arrow_repeats_after_a_delay_and_stops_on_release()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ScrollBar { height: 160; orientation: Qt.Vertical; size: 0.1; position: 0; stepSize: 0.01; policy: ScrollBar.AlwaysOn }
            """);
        using var _ = shell;
        var bar = (Quick.Controls.ScrollBar)root;
        bar.PointerPress(6, 156, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        Assert.Equal(0.01, bar.Position, 3);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0.01, bar.Position, 3);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(200));
        Assert.True(bar.Position > 0.02, $"position {bar.Position}");
        bar.PointerRelease(6, 156, QtEnums.LeftButton, 0, 0);
        var released = bar.Position;
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(released, bar.Position, 3);
    }

    [Fact]
    public void Paging_and_the_arrows_scroll_the_view()
    {
        var (root, _, shell) = Show("""
            import QtQuick
            import QtQuick.Controls
            ScrollView {
              width: 100; height: 100
              Item { implicitWidth: 50; implicitHeight: 400 }
            }
            """);
        using var _ = shell;
        var view = (ScrollView)root;
        var bar = ((ScrollBarAttached)view.GetAttached(typeof(ScrollBarAttached), o => new ScrollBarAttached(o))).Vertical!;
        // The track is the 68 pixels between the arrows, and the thumb a quarter of it.
        bar.PointerPress(6, 70, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        bar.PointerRelease(6, 70, QtEnums.LeftButton, 0, 0);
        Assert.Equal(100, view.Flickable.ContentY, 3);
        bar.PointerPress(6, 4, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        bar.PointerRelease(6, 4, QtEnums.LeftButton, 0, 0);
        Assert.Equal(60, view.Flickable.ContentY, 3);
    }
}
