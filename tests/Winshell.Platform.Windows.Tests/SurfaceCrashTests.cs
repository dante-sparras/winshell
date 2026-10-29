using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A real window whose drawing throws: the native window stops, a crash report is written, and a new native window
/// takes its place while the loop keeps running. The panel reserves no edge, and the report goes to a temp folder.
/// </summary>
public class SurfaceCrashTests
{
    /// <summary>An item whose first paint throws, as a bug in the runtime would.</summary>
    public sealed class PaintThrower : Item
    {
        public static int Paints;

        public override void Paint(SKCanvas canvas, PaintContext context)
        {
            if (Interlocked.Increment(ref Paints) == 1)
                throw new InvalidOperationException("the first paint fails");
            base.Paint(canvas, context);
        }
    }

    /// <summary>An item each of whose paints fails as a lost graphics device fails Present.</summary>
    public sealed class DeviceLoser : Item
    {
        public static int Paints;

        public override void Paint(SKCanvas canvas, PaintContext context)
        {
            Interlocked.Increment(ref Paints);
            throw new SharpGen.Runtime.SharpGenException(SharpGen.Runtime.Result.Fail);
        }
    }

    private const string Config = """
        import Winshell
        import Crashy
        ShellRoot {
          PanelWindow {
            anchors { top: true; left: true }
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 40
            implicitHeight: 40
            PaintThrower { anchors.fill: parent }
          }
        }
        """;

    [Fact]
    public void A_window_whose_paint_throws_is_reported_and_replaced_by_a_new_native_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, Config);
        var log = new Log { Sink = (_, _, _) => { } };
        PaintThrower.Paints = 0;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            var loopFailures = 0;
            loop.Failed += _ => loopFailures++;
            using var platform = new WindowsPlatform(loop, log);
            var crashes = new CrashReports(Path.Combine(dir, "crashes"));
            using var host = new ShellHost(root, loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Crashy").Type<PaintThrower>("PaintThrower");
                return modules;
            })
            { WatchFiles = false, Crashes = crashes };
            Assert.True(host.Start());
            var first = Assert.Single(platform.Windows);
            var firstHandle = first.Handle;
            var window = (ShellWindow)first.Client;

            nint replaced = 0;
            void Poll()
            {
                if (platform.Windows.SingleOrDefault() is { } now && now.Handle != firstHandle && now.IsVisible && PaintThrower.Paints >= 2)
                {
                    replaced = now.Handle;
                    loop.Quit(0);
                    return;
                }
                loop.Schedule(TimeSpan.FromMilliseconds(20), Poll);
            }
            loop.Schedule(TimeSpan.FromMilliseconds(20), Poll);
            loop.Schedule(TimeSpan.FromSeconds(10), () => loop.Quit(1));
            Assert.Equal(0, loop.Run());

            Assert.NotEqual(0, replaced);
            Assert.Same(window, Assert.Single(platform.Windows).Client);
            Assert.True(window.BackingWindowVisible);
            Assert.Equal(0, loopFailures);
            var report = File.ReadAllText(Assert.Single(Directory.GetFiles(crashes.Folder)));
            Assert.Contains("Where: PanelWindow in shell.qml", report, StringComparison.Ordinal);
            Assert.Contains("System.InvalidOperationException: the first paint fails", report, StringComparison.Ordinal);
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

    [Fact]
    public void Frames_the_device_cannot_draw_write_one_report_and_keep_the_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, Config.Replace("PaintThrower", "DeviceLoser", StringComparison.Ordinal));
        var log = new Log { Sink = (_, _, _) => { } };
        DeviceLoser.Paints = 0;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            var crashes = new CrashReports(Path.Combine(dir, "crashes"));
            using var host = new ShellHost(root, loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Crashy").Type<DeviceLoser>("DeviceLoser");
                return modules;
            })
            { WatchFiles = false, Crashes = crashes };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            var handle = window.Handle;
            var scene = ((ShellWindow)window.Client).Scene;

            // Ask for a frame every 20 ms, as an animation would, until ten frames have failed.
            void Next()
            {
                if (DeviceLoser.Paints >= 10)
                {
                    loop.Schedule(TimeSpan.FromMilliseconds(100), () => loop.Quit(0));
                    return;
                }
                scene.RequestUpdate();
                loop.Schedule(TimeSpan.FromMilliseconds(20), Next);
            }
            loop.Schedule(TimeSpan.FromMilliseconds(20), Next);
            loop.Schedule(TimeSpan.FromSeconds(10), () => loop.Quit(1));
            Assert.Equal(0, loop.Run());

            Assert.InRange(DeviceLoser.Paints, 10, int.MaxValue);
            Assert.Equal(handle, Assert.Single(platform.Windows).Handle);
            var report = File.ReadAllText(Assert.Single(Directory.GetFiles(crashes.Folder)));
            Assert.Contains("Where: PanelWindow in shell.qml", report, StringComparison.Ordinal);
            Assert.Contains(" drawing", report, StringComparison.Ordinal);
            Assert.Contains("SharpGen.Runtime.SharpGenException", report, StringComparison.Ordinal);
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
