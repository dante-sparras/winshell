using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Winshell.Native;

namespace Winshell.ShellTests;

public sealed class PanelWindowTests
{
    private static PanelWindow Panel(FrameworkElement content) => new()
    {
        Anchors = Edges.Top | Edges.Left,
        Margins = new Thickness(40, 40, 0, 0),
        Width = 320,
        Height = 60,
        Content = content,
    };

    [Fact]
    public async Task A_panel_lays_out_its_content_at_the_size_it_was_given()
    {
        var size = await ShellFixture.On(async _ =>
        {
            var content = new Grid();
            using var panel = Panel(content);
            panel.Visible = true;
            await ShellFixture.LaidOut(content);
            return (content.ActualWidth, content.ActualHeight, panel.ActualWidth, panel.ActualHeight);
        });

        Assert.Equal((320d, 60d, 320d, 60d), size);
    }

    [Fact]
    public async Task A_panel_keeps_the_styles_that_stop_it_taking_activation()
    {
        var styles = await ShellFixture.On(_ =>
        {
            using var panel = Panel(new Grid());
            panel.Visible = true;
            return (uint)Win32.GetWindowLongPtr(panel.Host!.Handle, Win32.GWL_EXSTYLE);
        });

        Assert.Equal(Win32.WS_EX_NOACTIVATE, styles & Win32.WS_EX_NOACTIVATE);
        Assert.Equal(Win32.WS_EX_TOOLWINDOW, styles & Win32.WS_EX_TOOLWINDOW);
        Assert.Equal(Win32.WS_EX_TOPMOST, styles & Win32.WS_EX_TOPMOST);
        Assert.Equal(Win32.WS_EX_NOREDIRECTIONBITMAP, styles & Win32.WS_EX_NOREDIRECTIONBITMAP);
    }

    [Fact]
    public async Task A_panel_that_cannot_take_focus_leaves_the_foreground_window_alone()
    {
        var unchanged = await ShellFixture.On(_ =>
        {
            var before = Win32.GetForegroundWindow();
            using var panel = Panel(new Grid());
            panel.Visible = true;
            panel.Focus();
            return Win32.GetForegroundWindow() == before;
        });

        Assert.True(unchanged);
    }

    [Fact]
    public async Task A_panel_draws_its_content()
    {
        var pixels = await ShellFixture.On(async _ =>
        {
            var content = new Grid { Background = new SolidColorBrush(Colors.Red) };
            using var panel = Panel(content);
            panel.Visible = true;
            await ShellFixture.LaidOut(content);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(content);
            var buffer = await bitmap.GetPixelsAsync();
            return (bitmap.PixelWidth, Bytes: buffer.ToArray());
        });

        Assert.True(pixels.PixelWidth > 0);
        // BGRA: a red that reached the compositor, not an empty buffer.
        Assert.Equal([0, 0, 255, 255], pixels.Bytes[..4]);
    }

    [Fact]
    public async Task A_disposed_panel_destroys_its_native_window()
    {
        var alive = await ShellFixture.On(_ =>
        {
            var panel = Panel(new Grid());
            panel.Visible = true;
            var handle = panel.Host!.Handle;
            panel.Dispose();
            return Win32.IsWindow(handle);
        });

        Assert.False(alive);
    }
}
