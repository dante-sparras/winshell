using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.ShellTests;

/// <summary>
/// Fullscreen uses a real window for a moment, then puts it back. When the work area is the whole monitor
/// (the taskbar hides itself), covering the monitor is the whole check: the window's rectangle is the monitor.
/// </summary>
[Trait("Category", "Slow")]
public sealed class FloatingWindowFullscreenTests
{
    [Fact]
    public async Task Fullscreen_covers_the_monitor_and_then_puts_the_frame_back()
    {
        await ShellFixture.On(_ =>
        {
            using var window = One();
            window.Visible = true;
            try
            {
                var hwnd = window.Host!.Handle;
                var before = Outer(hwnd);
                var style = Style(hwnd) & ~Win32.WS_VISIBLE;

                window.Fullscreen = true;

                Assert.True(window.Fullscreen);
                Assert.False(window.Maximized);
                AssertCoversMonitor(hwnd);

                window.Fullscreen = false;

                Assert.False(window.Fullscreen);
                Assert.Equal(before, Outer(hwnd));
                Assert.Equal(style, Style(hwnd) & ~Win32.WS_VISIBLE);
                AssertFramed(hwnd);
            }
            finally
            {
                window.Fullscreen = false;
            }

            return 0;
        });
    }

    [Fact]
    public async Task A_dpi_message_while_fullscreen_keeps_the_monitor_and_scales_the_old_place()
    {
        await ShellFixture.On(_ =>
        {
            using var window = One();
            window.Visible = true;
            try
            {
                var hwnd = window.Host!.Handle;
                var before = Outer(hwnd);
                var dpi = Win32.GetDpiForWindow(hwnd);
                Assert.NotEqual(0u, dpi);
                var nextDpi = dpi == 96 ? 192u : 96u;
                window.Fullscreen = true;
                AssertCoversMonitor(hwnd);

                var suggested = new RECT(5, 6, 7, 8);
                var memory = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
                try
                {
                    Marshal.StructureToPtr(suggested, memory, false);
                    var wParam = (nint)(nextDpi | (nextDpi << 16));
                    Win32.SendMessageW(hwnd, Win32.WM_DPICHANGED, wParam, memory);
                }
                finally
                {
                    Marshal.FreeHGlobal(memory);
                }

                Assert.True(window.Fullscreen);
                AssertCoversMonitor(hwnd);
                Assert.NotEqual(suggested, Outer(hwnd));

                window.Fullscreen = false;

                Assert.False(window.Fullscreen);
                AssertFramed(hwnd);
                var after = Outer(hwnd);
                var scale = nextDpi / (double)dpi;
                Assert.InRange(after.Width, (int)Math.Round(before.Width * scale) - 2,
                    (int)Math.Round(before.Width * scale) + 2);
                Assert.InRange(after.Height, (int)Math.Round(before.Height * scale) - 2,
                    (int)Math.Round(before.Height * scale) + 2);
            }
            finally
            {
                window.Fullscreen = false;
            }

            return 0;
        });
    }

    [Fact]
    public async Task Fullscreen_remembers_a_maximized_window()
    {
        await ShellFixture.On(_ =>
        {
            using var window = One();
            window.Visible = true;
            var hwnd = window.Host!.Handle;
            window.Maximized = true;
            Assert.True(Win32.IsZoomed(hwnd));
            Assert.True(window.Maximized);
            var restored = Restored(hwnd);

            window.Fullscreen = true;

            Assert.True(window.Fullscreen);
            Assert.False(window.Maximized);
            Assert.False(Win32.IsZoomed(hwnd));
            AssertCoversMonitor(hwnd);

            window.Fullscreen = false;

            Assert.False(window.Fullscreen);
            Assert.True(window.Maximized);
            Assert.True(Win32.IsZoomed(hwnd));
            AssertFramed(hwnd);
            Assert.Equal(restored, Restored(hwnd));
            return true;
        });
    }

    [Fact]
    public async Task Minimizing_a_fullscreen_window_keeps_fullscreen_and_maximized_leaves_it()
    {
        await ShellFixture.On(_ =>
        {
            using var window = One();
            window.Visible = true;
            var hwnd = window.Host!.Handle;
            var before = Outer(hwnd);

            window.Fullscreen = true;
            window.Minimized = true;
            Assert.True(window.Fullscreen);
            Assert.True(window.Minimized);
            Assert.True(Win32.IsIconic(hwnd));

            window.Minimized = false;
            Assert.True(window.Fullscreen);
            Assert.False(window.Minimized);
            Assert.False(Win32.IsIconic(hwnd));
            AssertCoversMonitor(hwnd);

            window.Maximized = true;
            Assert.False(window.Fullscreen);
            Assert.True(window.Maximized);
            Assert.True(Win32.IsZoomed(hwnd));
            AssertFramed(hwnd);

            window.Maximized = false;
            Assert.False(window.Fullscreen);
            Assert.Equal(before, Outer(hwnd));
            return true;
        });
    }

    [Fact]
    public async Task Fullscreen_from_a_minimized_window_shows_it_and_then_minimizes_it_again()
    {
        await ShellFixture.On(_ =>
        {
            using var window = One();
            window.Visible = true;
            var hwnd = window.Host!.Handle;
            window.Minimized = true;
            Assert.True(Win32.IsIconic(hwnd));

            window.Fullscreen = true;

            Assert.True(window.Fullscreen);
            Assert.False(window.Minimized);
            Assert.False(Win32.IsIconic(hwnd));
            AssertCoversMonitor(hwnd);

            window.Fullscreen = false;

            Assert.False(window.Fullscreen);
            Assert.True(window.Minimized);
            Assert.True(Win32.IsIconic(hwnd));
            AssertFramed(hwnd);
            return true;
        });
    }

    [Fact]
    public async Task An_owner_stays_under_the_window_and_can_be_changed()
    {
        await ShellFixture.On(async _ =>
        {
            using var owner = One();
            using var other = One();
            using var early = new FloatingWindow { Title = "Winshell test", Width = 320, Height = 200, Owner = owner };
            early.Visible = true;
            owner.Visible = true;
            other.Visible = true;
            using var child = new FloatingWindow { Title = "Winshell test", Width = 320, Height = 200, Owner = owner };
            child.Visible = true;
            var ownerHwnd = owner.Host!.Handle;
            var childHwnd = child.Host!.Handle;
            var otherHwnd = other.Host!.Handle;

            Assert.Equal(ownerHwnd, Win32.GetWindowLongPtr(childHwnd, Win32.GWLP_HWNDPARENT));
            Assert.Equal(ownerHwnd, Win32.GetWindow(childHwnd, Win32.GW_OWNER));
            Assert.Equal(ownerHwnd, Win32.GetWindowLongPtr(early.Host!.Handle, Win32.GWLP_HWNDPARENT));
            Assert.Equal(0u, Style(childHwnd) & WsChild);
            AssertAbove(childHwnd, ownerHwnd);

            owner.Owner = child;
            Assert.Null(owner.Owner);
            child.Owner = child;
            Assert.Same(owner, child.Owner);

            child.Owner = other;
            Assert.Same(other, child.Owner);
            Assert.Equal(otherHwnd, Win32.GetWindowLongPtr(childHwnd, Win32.GWLP_HWNDPARENT));
            AssertAbove(childHwnd, otherHwnd);

            child.Owner = null;
            Assert.Null(child.Owner);
            Assert.Equal((nint)0, Win32.GetWindowLongPtr(childHwnd, Win32.GWLP_HWNDPARENT));

            child.Owner = owner;
            owner.Minimized = true;
            Assert.True(Win32.IsIconic(ownerHwnd));
            await Wait.UntilAsync(() => !Win32.IsWindowVisible(childHwnd), "the owned window to hide with its owner");
            Assert.False(child.Minimized);
            owner.Minimized = false;
            Assert.False(Win32.IsIconic(ownerHwnd));
            await Wait.UntilAsync(() => Win32.IsWindowVisible(childHwnd), "the owned window to show with its owner");
            Assert.False(child.Minimized);
            return true;
        });
    }

    [Fact]
    public async Task Closing_the_owner_closes_the_owned_window_without_leaving_its_handle()
    {
        await ShellFixture.On(async _ =>
        {
            var owner = One();
            using var other = One();
            using var child = new FloatingWindow { Title = "Winshell test", Width = 320, Height = 200, Owner = owner };
            owner.Visible = true;
            other.Visible = true;
            child.Visible = true;
            var host = child.Host!;
            var hwnd = host.Handle;
            var otherHwnd = other.Host!.Handle;
            var closed = false;
            child.Closed += () => closed = true;

            owner.Dispose();

            Assert.False(Win32.IsWindow(hwnd));
            Assert.Equal(0, host.Handle);
            Assert.Null(child.Owner);
            Assert.True(Win32.IsWindow(otherHwnd));

            await Wait.UntilAsync(() => closed, "the owned window to say it closed");
            Assert.False(child.Visible);
            Assert.Null(child.Host);

            child.Visible = true;
            var again = child.Host!.Handle;
            Assert.NotEqual(hwnd, again);
            Assert.True(Win32.IsWindow(again));
            Assert.Equal((nint)0, Win32.GetWindowLongPtr(again, Win32.GWLP_HWNDPARENT));
            return true;
        });
    }

    private const uint WsChild = 0x40000000;

    private static RECT Restored(nint hwnd)
    {
        var place = new WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        Assert.True(Win32.GetWindowPlacement(hwnd, ref place));
        return place.Normal;
    }

    private static void AssertAbove(nint window, nint other)
    {
        const uint next = 2;
        for (var at = Win32.GetWindow(window, next); at != 0; at = Win32.GetWindow(at, next))
            if (at == other)
                return;
        Assert.Fail("The window is not above the other.");
    }

    private static FloatingWindow One()
    {
        return new FloatingWindow { Title = "Winshell test", Width = 320, Height = 200 };
    }

    private static void AssertCoversMonitor(nint hwnd)
    {
        var (_, bounds) = Screens.Of(hwnd);
        var rect = Outer(hwnd);
        Assert.InRange(rect.Left, bounds.Left - 1, bounds.Left + 1);
        Assert.InRange(rect.Top, bounds.Top - 1, bounds.Top + 1);
        Assert.InRange(rect.Right, bounds.Right - 1, bounds.Right + 1);
        Assert.InRange(rect.Bottom, bounds.Bottom - 1, bounds.Bottom + 1);
        Assert.Equal(0u, Style(hwnd) & (Win32.WS_CAPTION | Win32.WS_THICKFRAME));
        var monitor = Screens.Describe(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST));
        Assert.NotNull(monitor);
        // A taskbar that is showing makes the work area smaller. The window then extends past it.
        if (!monitor.Work.Equals(monitor.Bounds))
            Assert.True(
                rect.Left < monitor.Work.Left || rect.Top < monitor.Work.Top
                                              || rect.Right > monitor.Work.Right || rect.Bottom > monitor.Work.Bottom);
    }

    private static void AssertFramed(nint hwnd)
    {
        var style = Style(hwnd);
        Assert.NotEqual(0u, style & Win32.WS_CAPTION);
        Assert.NotEqual(0u, style & Win32.WS_THICKFRAME);
    }

    private static uint Style(nint hwnd)
    {
        return (uint)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
    }

    private static RECT Outer(nint hwnd)
    {
        Assert.True(Win32.GetWindowRect(hwnd, out var rect));
        return rect;
    }
}
