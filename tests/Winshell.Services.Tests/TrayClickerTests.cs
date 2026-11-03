using System.Runtime.InteropServices;
using Winshell.Native;
using Winshell.Tray;

namespace Winshell.Services.Tests;

public sealed class TrayClickerTests
{
    [Fact]
    public void An_elevated_shell_clicks_no_app_above_medium_integrity()
    {
        const uint low = 0x1000, high = 0x3000, system = 0x4000, unknown = uint.MaxValue;
        Assert.False(TrayClicker.MayClick(high, high));
        Assert.False(TrayClicker.MayClick(high, system));
        Assert.False(TrayClicker.MayClick(high, unknown));
        Assert.True(TrayClicker.MayClick(high, TrayClicker.MediumIntegrity));
        Assert.True(TrayClicker.MayClick(high, low));
        // Unelevated, Windows keeps the shell out of elevated apps itself, as it does Explorer.
        Assert.True(TrayClicker.MayClick(TrayClicker.MediumIntegrity, high));
        Assert.True(TrayClicker.MayClick(TrayClicker.MediumIntegrity, TrayClicker.MediumIntegrity));

        Assert.InRange(TrayClicker.IntegrityLevel((uint)Environment.ProcessId), low, system);
        // The System Idle Process has no token to read.
        Assert.Equal(unknown, TrayClicker.IntegrityLevel(0));
    }

    [Fact]
    public void A_click_hands_over_the_foreground_then_reaches_the_app_only_when_this_process_may_click_it()
    {
        // The app is this test process: medium on a developer's desktop, elevated on CI. Its window is message-only,
        // so nothing else can find it.
        var app = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, 0, 0);
        Assert.NotEqual(0, app);
        var seen = new List<string>();
        WindowProc record = (hwnd, msg, wParam, lParam) =>
        {
            if (msg == 0x8001)
                seen.Add($"callback {lParam:X}");
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        };
        var previous = SetWindowLongPtrW(app, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(record));
        // The real handoff joins the user's foreground thread, so a recorder stands in for it.
        using var clicker = new TrayClicker(new ManualEventLoop(), (window, pid) => seen.Add($"foreground {window} {pid}"));
        try
        {
            clicker.Click(new TrayIcon { Key = "app", Window = app, Id = 1, CallbackMessage = 0x8001 }, TrayClick.Left, null);
        }
        finally
        {
            SetWindowLongPtrW(app, GWLP_WNDPROC, previous);
            Win32.DestroyWindow(app);
            GC.KeepAlive(record);
        }

        // An app before version 4 is told the mouse message: WM_LBUTTONDOWN, then WM_LBUTTONUP.
        var own = TrayClicker.IntegrityLevel((uint)Environment.ProcessId);
        string[] expected = TrayClicker.MayClick(own, own) ? [$"foreground {app} {Environment.ProcessId}", "callback 201", "callback 202"] : [];
        Assert.Equal(expected, seen);
    }

    private delegate nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private const int GWLP_WNDPROC = -4;

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
}
