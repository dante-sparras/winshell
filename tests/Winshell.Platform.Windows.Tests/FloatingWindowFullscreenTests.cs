using System.Runtime.InteropServices;
using Winshell.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Fullscreen and owner use a real window for a moment, then put it back. When the work area is the whole monitor
/// (the taskbar hides itself), covering the monitor is the whole check: the window's rectangle is the monitor.
/// </summary>
public class FloatingWindowFullscreenTests
{
    private const uint WsChild = 0x40000000;

    private const string OneWindow = """
        import Winshell
        ShellRoot {
          FloatingWindow {
            objectName: "window"
            title: "Winshell test"
            implicitWidth: 320
            implicitHeight: 200
          }
        }
        """;

    private const string OwnedWindows = """
        import Winshell
        ShellRoot {
          FloatingWindow {
            objectName: "early"
            visible: false
            parentWindow: owner
            implicitWidth: 140
            implicitHeight: 100
          }
          FloatingWindow {
            objectName: "child"
            parentWindow: owner
            title: "Winshell test"
            implicitWidth: 180
            implicitHeight: 120
          }
          FloatingWindow {
            id: owner
            objectName: "owner"
            title: "Winshell test"
            implicitWidth: 220
            implicitHeight: 140
          }
          FloatingWindow {
            objectName: "other"
            title: "Winshell test"
            implicitWidth: 160
            implicitHeight: 100
          }
        }
        """;

    [Fact]
    public void A_saved_place_follows_a_moved_monitor_and_a_new_scale()
    {
        var saved = new SavedFloatingPlace(
            Win32.WS_OVERLAPPEDWINDOW, 1, new RECT(0, 0, 1920, 1080), @"\\.\DISPLAY1",
            100, 80, 400, 300, false, false, default, false);

        Assert.True(saved.Matches(@"\\.\DISPLAY1", new RECT(0, 0, 1920, 1080), 1));
        Assert.False(saved.Matches(@"\\.\DISPLAY1", new RECT(1920, 0, 3840, 1080), 1));
        Assert.False(saved.Matches(@"\\.\DISPLAY2", new RECT(0, 0, 1920, 1080), 1));
        Assert.False(saved.Matches(@"\\.\DISPLAY1", new RECT(0, 0, 1920, 1080), 2));
        // Logical (100, 80, 400, 300) on a monitor that starts at (1920, 0), still at scale 1.
        Assert.Equal(new RECT(2020, 80, 2420, 380), saved.RectOn(new RECT(1920, 0, 3840, 1080), 1));
        // The same logical place at scale 2 is twice the physical size, from the same corner.
        Assert.Equal(new RECT(200, 160, 1000, 760), saved.RectOn(new RECT(0, 0, 1920, 1080), 2));
    }

    [Fact]
    public void Fullscreen_covers_the_monitor_and_then_puts_the_frame_back()
    {
        Run(OneWindow, (_, platform, host) =>
        {
            var window = Named(host, "window");
            var hwnd = Native(platform, window).Handle;
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
        });
    }

    [Fact]
    public void Fullscreen_remembers_a_maximized_window()
    {
        Run(OneWindow, (_, platform, host) =>
        {
            var window = Named(host, "window");
            var hwnd = Native(platform, window).Handle;
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
        });
    }

    [Fact]
    public void Minimizing_a_fullscreen_window_keeps_fullscreen_and_maximized_leaves_it()
    {
        Run(OneWindow, (_, platform, host) =>
        {
            var window = Named(host, "window");
            var hwnd = Native(platform, window).Handle;
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
        });
    }

    [Fact]
    public void Fullscreen_from_a_minimized_window_shows_it_and_then_minimizes_it_again()
    {
        Run(OneWindow, (_, platform, host) =>
        {
            var window = Named(host, "window");
            var hwnd = Native(platform, window).Handle;
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
        });
    }

    [Fact]
    public void A_dpi_message_while_fullscreen_keeps_the_monitor_and_scales_the_old_place()
    {
        Run(OneWindow, (_, platform, host) =>
        {
            var window = Named(host, "window");
            var hwnd = Native(platform, window).Handle;
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
            Assert.InRange(after.Width, (int)Math.Round(before.Width * scale) - 2, (int)Math.Round(before.Width * scale) + 2);
            Assert.InRange(after.Height, (int)Math.Round(before.Height * scale) - 2, (int)Math.Round(before.Height * scale) + 2);
        });
    }

    [Fact]
    public void An_owner_stays_under_the_window_and_can_be_changed()
    {
        Run(OwnedWindows, (_, platform, host) =>
        {
            var owner = Named(host, "owner");
            var child = Named(host, "child");
            var other = Named(host, "other");
            var early = Named(host, "early");
            var ownerHwnd = Native(platform, owner).Handle;
            var childHwnd = Native(platform, child).Handle;
            var otherHwnd = Native(platform, other).Handle;
            var earlyHwnd = Native(platform, early).Handle;

            Assert.Equal(ownerHwnd, Win32.GetWindowLongPtr(childHwnd, Win32.GWLP_HWNDPARENT));
            Assert.Equal(ownerHwnd, Win32.GetWindow(childHwnd, Win32.GW_OWNER));
            Assert.Equal(ownerHwnd, Win32.GetWindowLongPtr(earlyHwnd, Win32.GWLP_HWNDPARENT));
            Assert.Equal(0u, Style(childHwnd) & WsChild);
            AssertAbove(childHwnd, ownerHwnd);

            owner.ParentWindow = child;
            Assert.Null(owner.ParentWindow);
            child.ParentWindow = child;
            Assert.Equal(owner, child.ParentWindow);

            child.ParentWindow = other;
            Assert.Equal(other, child.ParentWindow);
            Assert.Equal(otherHwnd, Win32.GetWindowLongPtr(childHwnd, Win32.GWLP_HWNDPARENT));
            AssertAbove(childHwnd, otherHwnd);

            child.ParentWindow = null;
            Assert.Null(child.ParentWindow);
            Assert.Equal((nint)0, Win32.GetWindowLongPtr(childHwnd, Win32.GWLP_HWNDPARENT));

            child.ParentWindow = owner;
            owner.Minimized = true;
            Assert.True(Win32.IsIconic(ownerHwnd));
            Wait.Until(() => !Win32.IsWindowVisible(childHwnd), "the owned window to hide with its owner", Pump);
            Assert.False(child.Minimized);
            owner.Minimized = false;
            Assert.False(Win32.IsIconic(ownerHwnd));
            Wait.Until(() => Win32.IsWindowVisible(childHwnd), "the owned window to show with its owner", Pump);
            Assert.False(child.Minimized);
        });
    }

    [Fact]
    public void Closing_the_owner_closes_the_owned_window_without_leaving_its_handle()
    {
        Run(OwnedWindows, (loop, platform, host) =>
        {
            var owner = Named(host, "owner");
            var child = Named(host, "child");
            var other = Named(host, "other");
            var childNative = Native(platform, child);
            var hwnd = childNative.Handle;
            var otherHwnd = Native(platform, other).Handle;
            var closed = false;
            child.Connect("closed", _ => closed = true);

            owner.Destroy();

            Assert.False(Win32.IsWindow(hwnd));
            Assert.Equal(0, childNative.Handle);
            Assert.Null(child.ParentWindow);
            Assert.DoesNotContain(platform.Windows, w => w.Handle == hwnd || w.Handle == 0 || !Win32.IsWindow(w.Handle));
            Assert.True(Win32.IsWindow(otherHwnd));

            loop.Schedule(TimeSpan.FromSeconds(10), () => loop.Quit(1));
            loop.Post(() => loop.Quit(closed ? 0 : 1));
            Assert.Equal(0, loop.Run());
            Assert.True(closed);
            Assert.False(child.Visible);
            Assert.Null(child.Backend);

            child.Visible = true;
            var again = Native(platform, child);
            Assert.NotEqual(hwnd, again.Handle);
            Assert.True(Win32.IsWindow(again.Handle));
            Assert.Equal((nint)0, Win32.GetWindowLongPtr(again.Handle, Win32.GWLP_HWNDPARENT));
        });
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
        {
            Assert.True(
                rect.Left < monitor.Work.Left || rect.Top < monitor.Work.Top
                || rect.Right > monitor.Work.Right || rect.Bottom > monitor.Work.Bottom);
        }
    }

    private static void AssertFramed(nint hwnd)
    {
        var style = Style(hwnd);
        Assert.NotEqual(0u, style & Win32.WS_CAPTION);
        Assert.NotEqual(0u, style & Win32.WS_THICKFRAME);
    }

    private static void Pump()
    {
        while (Win32.PeekMessageW(out var message, 0, 0, 0, Win32.PM_REMOVE))
            Win32.DispatchMessageW(message);
    }

    private static void AssertAbove(nint upper, nint lower)
    {
        // GW_HWNDNEXT walks down the z-order, so the owner comes after the windows between them.
        var hwnd = upper;
        for (var hop = 0; hwnd != 0 && hop < 4096; hop++)
        {
            hwnd = Win32.GetWindow(hwnd, Win32.GW_HWNDNEXT);
            if (hwnd == lower)
                return;
        }
        Assert.Fail("The owned window is not above its owner.");
    }

    private static uint Style(nint hwnd) => (uint)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);

    private static RECT Outer(nint hwnd)
    {
        Assert.True(Win32.GetWindowRect(hwnd, out var rect));
        return rect;
    }

    private static RECT Restored(nint hwnd)
    {
        var placement = new Winshell.Native.WINDOWPLACEMENT { Length = (uint)Marshal.SizeOf<Winshell.Native.WINDOWPLACEMENT>() };
        Assert.True(Win32.GetWindowPlacement(hwnd, ref placement));
        return placement.Normal;
    }

    private static FloatingWindow Named(ShellHost host, string name) =>
        host.Root!.OwnedObjects.OfType<FloatingWindow>().Single(w => w.ObjectName == name);

    private static Win32Window Native(WindowsPlatform platform, ShellWindow window) =>
        Assert.Single(platform.Windows, w => ReferenceEquals(w.Client, window));

    private static void Run(string config, Action<WinEventLoop, WindowsPlatform, ShellHost> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), config);
        var log = new Log { Sink = (_, _, _) => { } };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            try
            {
                test(loop, platform, host);
            }
            finally
            {
                if (host.Root is not null)
                {
                    foreach (var window in host.Root.OwnedObjects.OfType<FloatingWindow>())
                    {
                        if (!window.IsDestroyed)
                            window.Fullscreen = false;
                    }
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
