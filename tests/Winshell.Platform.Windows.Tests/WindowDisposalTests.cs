using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

public class WindowDisposalTests
{
    public sealed class PaintAction : Item
    {
        public static Action<SKCanvas>? Once;

        public override void Paint(SKCanvas canvas, PaintContext context)
        {
            Interlocked.Exchange(ref Once, null)?.Invoke(canvas);
            base.Paint(canvas, context);
        }
    }

    [Fact]
    public void Destroying_a_window_during_its_frame_releases_its_native_resources_after_paint_returns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "shell.qml");
        File.WriteAllText(path, """
            import Winshell
            import Probe
            ShellRoot {
              PanelWindow {
                anchors { top: true; left: true }
                exclusionMode: ExclusionMode.Ignore
                implicitWidth: 120
                implicitHeight: 40
                PaintAction { anchors.fill: parent }
              }
            }
            """);
        var errors = new List<string>();
        var log = new Log { Sink = (level, category, message) => { if (level >= LogLevel.Error) errors.Add($"{category}: {message}"); } };
        PaintAction.Once = null;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(path, loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Probe").Type<PaintAction>("PaintAction");
                return modules;
            }) { WatchFiles = false };
            Assert.True(host.Start());
            var backend = Assert.Single(platform.Windows);
            var window = (ShellWindow)backend.Client;
            var painted = false;
            var aliveDuringPaint = false;
            loop.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                PaintAction.Once = canvas =>
                {
                    painted = true;
                    window.Destroy();
                    aliveDuringPaint = backend.Handle != 0;
                    // On a broken build, abort before Skia writes into the bitmap Dispose just freed.
                    if (!aliveDuringPaint)
                        throw new InvalidOperationException("The native window was disposed inside its paint.");
                    canvas.Clear(SKColors.CornflowerBlue);
                    // A repeated dispose request in the same frame must still release it only once.
                    backend.Dispose();
                };
                window.Scene.RequestUpdate();
            });
            loop.Schedule(TimeSpan.FromMilliseconds(500), () => loop.Quit(0));
            loop.Run();

            Assert.True(painted);
            Assert.True(aliveDuringPaint, "The native window and its surface were released before paint returned.");
            Assert.Equal(0, backend.Handle);
            Assert.Empty(platform.Windows);
            Assert.Empty(errors);
        }
        finally
        {
            PaintAction.Once = null;
            Directory.Delete(directory, recursive: true);
        }
    }
}
