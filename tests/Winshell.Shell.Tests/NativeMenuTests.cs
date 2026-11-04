using System.Runtime.InteropServices;
using Winshell.Native;
using Winshell.TestSupport;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed partial class NativeMenuTests
{
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint MF_STRING = 0x0000;

    /// <summary>
    /// A tray app that opens its menu without the foreground has a menu a click elsewhere never closes. The thread
    /// here is that app: it has no foreground, and its menu still has to go.
    /// </summary>
    [Fact]
    public async Task A_menu_opened_without_the_foreground_is_closed()
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uint menuThread = 0;
        var app = new Thread(() =>
        {
            menuThread = Win32.GetCurrentThreadId();
            var owner = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var menu = CreatePopupMenu();
            try
            {
                _ = AppendMenuW(menu, MF_STRING, 1, "Item");
                _ = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY, 0, 0, owner, 0);
                closed.SetResult();
            }
            finally
            {
                _ = DestroyMenu(menu);
                _ = Win32.DestroyWindow(owner);
            }
        })
        { IsBackground = true, Name = "Tray app" };
        var started = TimeProvider.System.GetTimestamp();
        app.SetApartmentState(ApartmentState.STA);
        app.Start();
        Wait.Until(() => menuThread != 0 && MenuShowsOn(menuThread), "the app's menu to open");
        var opened = TimeProvider.System.GetElapsedTime(started);
        var before = State(menuThread);

        NativeMenu.Dismiss();

        // This failed once on CI and never here, so a menu that stays open says what its thread was doing.
        try
        {
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"The menu stayed open. It took {opened.TotalMilliseconds:F0} ms to open. Its thread before the cancel: {before}. After: {State(menuThread)}.");
        }
    }

    /// <summary>What Windows says about a thread's menu: whether one shows, and the thread's menu mode, owner and capture.</summary>
    private static string State(uint thread)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        var known = GetGUIThreadInfo(thread, ref info);
        return $"menu shows {MenuShowsOn(thread)}, info read {known}, flags 0x{info.Flags:x}, owner {info.MenuOwner != 0}, capture {info.Capture != 0}";
    }

    private static bool MenuShowsOn(uint thread)
    {
        nint menu = 0;
        while ((menu = FindWindowExW(0, menu, "#32768", null)) != 0)
        {
            if (Win32.IsWindowVisible(menu) && Win32.GetWindowThreadProcessId(menu, out _) == thread)
                return true;
        }
        return false;
    }

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint childAfter, string? className, string? windowName);
}
