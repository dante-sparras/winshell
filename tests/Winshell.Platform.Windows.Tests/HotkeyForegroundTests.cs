using Winshell.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A focusable shell window asks for the keyboard. The other window is this test's, off the screen, and it stops
/// pumping so the request is the case a low-level hook hits. The desktop's own windows are not moved.
/// </summary>
public class HotkeyForegroundTests
{
    private const string Config = """
        import Winshell
        import QtQuick
        PanelWindow {
          anchors.bottom: true
          anchors.left: true
          exclusionMode: ExclusionMode.Ignore
          implicitWidth: 48
          implicitHeight: 48
          focusable: true
          color: "transparent"
          MouseArea { objectName: "probe"; width: 40; height: 40; activeFocusOnTab: true }
        }
        """;

    [Fact]
    public void Taking_the_keyboard_returns_promptly_when_another_window_is_in_front()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var log = new Log { Sink = (_, _, _) => { } };
        using var hung = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        nint hungWindow = 0;
        var app = new Thread(() =>
        {
            hungWindow = Win32.CreateWindowExW(Win32.WS_EX_TOOLWINDOW, "STATIC", "Winshell test: foreground", Win32.WS_POPUP | Win32.WS_VISIBLE, -4000, -4000, 80, 40, 0, 0, 0, 0);
            Win32.SetForegroundWindow(hungWindow);
            hung.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Win32.DestroyWindow(hungWindow);
        })
        { IsBackground = true };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var took = TimeSpan.MaxValue;
            nint shell = 0;
            nint foreground = 0;
            loop.Schedule(TimeSpan.FromMilliseconds(200), () =>
            {
                app.Start();
                hung.Wait(TimeSpan.FromSeconds(5));
                var window = Assert.Single(platform.Windows);
                shell = window.Handle;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                window.SetKeyboardFocus(true);
                took = watch.Elapsed;
                foreground = Win32.GetForegroundWindow();
                loop.Quit(0);
            });
            loop.Schedule(TimeSpan.FromSeconds(8), () => loop.Quit(1));
            Assert.Equal(0, loop.Run());
            Assert.True(took < TimeSpan.FromSeconds(1), $"SetKeyboardFocus took {took.TotalMilliseconds:0} ms");
            Assert.Equal(shell, foreground);
        }
        finally
        {
            release.Set();
            if (app.IsAlive)
                app.Join(TimeSpan.FromSeconds(5));
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
