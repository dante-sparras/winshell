using System.Diagnostics;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;
using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// UI Automation focusing an item in a real shell window while the app in front has stopped answering messages. The
/// app is a window of this test, on a thread that stops pumping; nothing else on the desktop is touched.
/// </summary>
public sealed class AutomationFocusTests(ITestOutputHelper output)
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
          MouseArea {
            objectName: "probe"
            width: 40
            height: 40
            activeFocusOnTab: true
            Accessible.role: Accessible.Button
            Accessible.name: "Probe"
          }
        }
        """;

    [Fact]
    public void Focusing_an_item_returns_promptly_and_the_shell_keeps_running_while_the_app_in_front_hangs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), Config);
        var log = new Log();
        using var hung = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        nint hungWindow = 0;
        var app = new Thread(() =>
        {
            // Off the screen, and never pumped until the test is done with it.
            hungWindow = Win32.CreateWindowExW(Win32.WS_EX_TOOLWINDOW, "STATIC", "Winshell test: an app that stopped answering", Win32.WS_POPUP | Win32.WS_VISIBLE, -4000, -4000, 80, 40, 0, 0, 0, 0);
            Win32.SetForegroundWindow(hungWindow);
            hung.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Win32.DestroyWindow(hungWindow);
        });
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            WindowsModules.Install(platform);
            using var host = new ShellHost(Path.Combine(dir, "shell.qml"), loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());

            var elapsed = TimeSpan.MaxValue;
            var hr = 0;
            var appInFront = false;
            var focused = false;
            var heartbeats = 0;
            loop.Schedule(TimeSpan.FromMilliseconds(300), () =>
            {
                app.Start();
                hung.Wait(TimeSpan.FromSeconds(10));
                appInFront = Win32.GetForegroundWindow() == hungWindow;
                var window = platform.Windows.Single();
                var scene = ((ShellWindow)window.Client).Scene;
                var probe = Find(scene.Root, "probe");
                var element = window.Automation.ElementFor(probe);
                Assert.NotNull(element);
                var watch = Stopwatch.StartNew();
                hr = ((IRawElementProviderFragment)element).SetFocus();
                elapsed = watch.Elapsed;
                focused = scene.FocusItem == probe;
                // The shell's loop still turns afterwards, while the app is still hung.
                loop.Schedule(TimeSpan.FromMilliseconds(50), () => heartbeats++);
                loop.Schedule(TimeSpan.FromMilliseconds(100), () => heartbeats++);
                loop.Schedule(TimeSpan.FromMilliseconds(300), () => loop.Quit(0));
            });
            loop.Run();

            output.WriteLine($"app in front: {appInFront}; SetFocus took {elapsed.TotalMilliseconds:0} ms and returned 0x{hr:x8}");
            Assert.True(elapsed < TimeSpan.FromSeconds(1), $"SetFocus took {elapsed.TotalMilliseconds:0} ms");
            Assert.Equal(2, heartbeats);
            Assert.True(focused);
            // Either the panel got the keyboard, or the client hears that it did not.
            Assert.Contains(hr, new[] { Uia.S_OK, Uia.InvalidOperation });
        }
        finally
        {
            release.Set();
            if (app.IsAlive)
                app.Join(TimeSpan.FromSeconds(10));
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Item Find(Item item, string name) =>
        item.ObjectName == name ? item : item.ChildItems.Select(c => Find(c, name)).FirstOrDefault(i => i is not null)!;
}
