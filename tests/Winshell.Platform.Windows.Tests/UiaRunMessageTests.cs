using System.Diagnostics;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Native;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A stray <c>WM_APP+2</c>, or the registered UI Automation message with a key this window did not issue, used to be
/// read as a GCHandle. The window has to stay up.
/// </summary>
public class UiaRunMessageTests
{
    private const string Config = """
        import Winshell
        ShellRoot {
          PanelWindow {
            visible: false
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 40
            implicitHeight: 20
          }
        }
        """;

    [Fact]
    public void A_stray_run_message_leaves_the_window_up_and_a_real_call_still_runs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, Config);
        var log = new Log { Sink = (_, _, _) => { } };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(root, loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            var hwnd = window.Handle;
            var automation = window.Automation;
            Assert.NotEqual(0u, WindowAutomation.RunMessage);
            Assert.NotEqual(Win32.WM_APP + 2, WindowAutomation.RunMessage);

            var done = new ManualResetEventSlim(false);
            Exception? failure = null;
            var hr = -1;
            loop.Schedule(TimeSpan.FromMilliseconds(50), () =>
            {
                try
                {
                    // These used to dereference lParam. Zero throws; a garbage handle reads wild memory.
                    automation.RunPosted(0);
                    automation.RunPosted(0x1234);
                    Assert.True(Win32.PostMessageW(hwnd, Win32.WM_APP + 2, 0, 0));
                    Assert.True(Win32.PostMessageW(hwnd, Win32.WM_APP + 2, 0, 0x1234));
                    Assert.True(Win32.PostMessageW(hwnd, WindowAutomation.RunMessage, 0, 0));
                    Assert.True(Win32.PostMessageW(hwnd, WindowAutomation.RunMessage, 0, 0x1234));
                    Win32.SendMessageW(hwnd, WindowAutomation.RunMessage, 0, 0);
                    Win32.SendMessageW(hwnd, WindowAutomation.RunMessage, 0, 0x1234);
                }
                catch (Exception e)
                {
                    failure = e;
                    done.Set();
                }
            });
            var started = 0L;
            loop.Schedule(TimeSpan.FromMilliseconds(200), () =>
            {
                var worker = new Thread(() =>
                {
                    try
                    {
                        hr = automation.Run(() => 7);
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                    finally
                    {
                        done.Set();
                    }
                })
                { IsBackground = true };
                started = Stopwatch.GetTimestamp();
                worker.Start();
            });
            void Watch()
            {
                if (done.IsSet)
                    loop.Quit(0);
                // Longer than the call's own timeout, so a call that times out fails on its result.
                // This only stops a call that never returns.
                else if (started != 0 && Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(45))
                    loop.Quit(1);
                else
                    loop.Schedule(TimeSpan.FromMilliseconds(50), Watch);
            }
            loop.Schedule(TimeSpan.FromMilliseconds(250), Watch);
            Assert.Equal(0, loop.Run());

            Assert.Null(failure);
            Assert.True(done.IsSet);
            Assert.Equal(7, hr);
            Assert.Equal(hwnd, Assert.Single(platform.Windows).Handle);
            Assert.True(NativeMethods.IsWindow(hwnd));
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
