using System.Reflection;
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
            // Waits for the end state, so a stall on a slow runner takes longer instead of quitting before the frame.
            void QuitOnceReleased()
            {
                if (painted && backend.Handle == 0)
                    loop.Quit(0);
                else
                    loop.Schedule(TimeSpan.FromMilliseconds(10), QuitOnceReleased);
            }
            QuitOnceReleased();
            loop.Schedule(TimeSpan.FromSeconds(10), () => loop.Quit(1));
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

    [Fact]
    public void A_popup_destroyed_while_being_shown_does_not_keep_the_mouse_grab()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "shell.qml");
        File.WriteAllText(path, """
            import Winshell
            import Probe
            ShellRoot {
              PanelWindow {
                id: bar
                anchors { top: true; left: true }
                exclusionMode: ExclusionMode.Ignore
                implicitWidth: 120
                implicitHeight: 40
              }
              PopupWindow {
                anchor.window: bar
                grabFocus: true
                visible: false
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
            var popup = Assert.Single(host.Root!.OwnedObjects.OfType<PopupWindow>());
            var backend = Assert.Single(platform.Windows, w => ReferenceEquals(w.Client, popup));
            var painted = false;
            loop.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                PaintAction.Once = _ =>
                {
                    painted = true;
                    popup.Destroy();
                };
                popup.Visible = true;
            });
            loop.Schedule(TimeSpan.FromMilliseconds(500), () => loop.Quit(0));
            loop.Run();

            Assert.True(painted);
            Assert.Equal(0, backend.Handle);
            Assert.Single(platform.Windows);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Empty(Assert.IsType<HashSet<Win32Window>>(typeof(WindowsPlatform).GetField("_grabbing", fields)!.GetValue(platform)));
            Assert.Equal((nint)0, Assert.IsType<nint>(typeof(WindowsPlatform).GetField("_mouseHook", fields)!.GetValue(platform)));
            Assert.Empty(errors);
        }
        finally
        {
            PaintAction.Once = null;
            Directory.Delete(directory, recursive: true);
        }
    }
}
