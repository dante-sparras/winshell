using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A window that stopped answering times a UI Automation call out, so the screen reader isn't stuck with it.</summary>
public class UiaCallTimeoutTests
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
    public void A_call_to_a_stalled_window_times_out_and_the_next_call_runs()
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
            var automation = Assert.Single(platform.Windows).Automation;
            automation.CallTimeout = 100;

            var asked = new ManualResetEventSlim(false);
            var finished = new ManualResetEventSlim(false);
            var stalled = -1;
            var ran = false;
            var worker = new Thread(() =>
            {
                asked.Set();
                stalled = automation.Run(() =>
                {
                    ran = true;
                    return 7;
                });
                finished.Set();
            })
            { IsBackground = true };
            var after = -1;
            loop.Post(() =>
            {
                worker.Start();
                asked.Wait();
                // The loop is held here, so the call can't be answered until the worker has given up.
                Assert.True(finished.Wait(TimeSpan.FromSeconds(10)), "The call did not time out.");
                var next = new Thread(() =>
                {
                    automation.CallTimeout = 3000;
                    after = automation.Run(() => 7);
                    loop.Post(() => loop.Quit(0));
                })
                { IsBackground = true };
                next.Start();
                // After the stall. A timer armed before it becomes due during the wait and quits in the same
                // batch as this callback, before the next call is dispatched.
                loop.Schedule(TimeSpan.FromSeconds(45), () => loop.Quit(1));
            });

            Assert.Equal(0, loop.Run());
            Assert.Equal(Uia.Timeout, stalled);
            Assert.False(ran);
            Assert.Equal(7, after);
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
