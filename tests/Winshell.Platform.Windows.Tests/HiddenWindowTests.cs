using System.Reflection;
using SkiaSharp;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A hidden window draws nothing and holds no swap chain or bitmap. A change to its scene waits until it shows,
/// and showing draws the current scene before Windows shows the window, so it never shows stale or empty content.
/// </summary>
public class HiddenWindowTests
{
    /// <summary>Counts the frames its window draws, and whether Windows showed the window at the first one.</summary>
    public sealed class PaintCounter : Item
    {
        public static int Paints;
        public static nint Window;
        public static bool? ShownAtFirstPaint;

        public override void Paint(SKCanvas canvas, PaintContext context)
        {
            if (Paints++ == 0 && Window != 0)
                ShownAtFirstPaint = Win32.IsWindowVisible(Window);
            base.Paint(canvas, context);
        }
    }

    [Fact]
    public void A_hidden_popup_draws_only_once_it_shows_and_lets_go_of_its_buffers_when_it_hides()
    {
        const string config = """
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
                visible: false
                implicitWidth: 120
                implicitHeight: 40
                PaintCounter { anchors.fill: parent }
              }
            }
            """;
        Run(config, (loop, platform, host) =>
        {
            var popup = Assert.Single(host.Root!.OwnedObjects.OfType<PopupWindow>());
            var backend = Assert.Single(platform.Windows, w => ReferenceEquals(w.Client, popup));
            PaintCounter.Window = backend.Handle;
            int? hiddenPaints = null;
            bool? hiddenBuffers = null;
            int? shownPaints = null;
            bool? shownBuffers = null;
            bool? releasedBuffers = null;
            loop.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                // A resize and a scene change while hidden.
                popup.ImplicitWidth = 200;
                popup.Scene.RequestUpdate();
            });
            loop.Schedule(TimeSpan.FromMilliseconds(300), () =>
            {
                hiddenPaints = PaintCounter.Paints;
                hiddenBuffers = HoldsBuffers(backend);
                popup.Visible = true;
                shownPaints = PaintCounter.Paints;
                shownBuffers = HoldsBuffers(backend);
            });
            loop.Schedule(TimeSpan.FromMilliseconds(500), () =>
            {
                popup.Visible = false;
                releasedBuffers = HoldsBuffers(backend);
                loop.Quit(0);
            });

            Assert.Equal(0, loop.Run());
            Assert.Equal(0, hiddenPaints);
            Assert.False(hiddenBuffers);
            Assert.Equal(1, shownPaints);
            Assert.False(PaintCounter.ShownAtFirstPaint);
            Assert.True(shownBuffers);
            Assert.False(releasedBuffers);
        });
    }

    [Fact]
    public void A_running_busy_indicator_stops_asking_for_frames_while_its_window_is_hidden()
    {
        const string config = """
            import QtQuick.Controls
            import Winshell
            import Probe
            ShellRoot {
              PanelWindow {
                anchors { top: true; left: true }
                exclusionMode: ExclusionMode.Ignore
                implicitWidth: 120
                implicitHeight: 40
                BusyIndicator { running: true; width: 32; height: 32 }
                PaintCounter { anchors.fill: parent }
              }
            }
            """;
        Run(config, (loop, platform, host) =>
        {
            var window = (ShellWindow)Assert.Single(platform.Windows).Client;
            int atHide = 0, afterHidden = 0, atShow = 0;
            loop.Schedule(TimeSpan.FromMilliseconds(300), () =>
            {
                window.Visible = false;
                atHide = PaintCounter.Paints;
            });
            loop.Schedule(TimeSpan.FromMilliseconds(600), () =>
            {
                afterHidden = PaintCounter.Paints;
                window.Visible = true;
                atShow = PaintCounter.Paints;
            });
            loop.Schedule(TimeSpan.FromMilliseconds(900), () => loop.Quit(0));

            Assert.Equal(0, loop.Run());
            // It draws frame after frame while shown, none while hidden, and again once shown.
            Assert.True(atHide > 3, $"{atHide} frames while shown");
            Assert.Equal(atHide, afterHidden);
            Assert.True(PaintCounter.Paints > atShow + 3, $"{PaintCounter.Paints - atShow} frames after showing again");
        });
    }

    private static bool HoldsBuffers(Win32Window window)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var surface = typeof(Win32Window).GetField("_surface", fields)!.GetValue(window)!;
        return typeof(CompositionSurface).GetField("_swapChain", fields)!.GetValue(surface) is not null
            || typeof(CompositionSurface).GetField("_bitmap", fields)!.GetValue(surface) is not null;
    }

    private static void Run(string config, Action<WinEventLoop, WindowsPlatform, ShellHost> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, config);
        var log = new Log { Sink = (_, _, _) => { } };
        PaintCounter.Paints = 0;
        PaintCounter.Window = 0;
        PaintCounter.ShownAtFirstPaint = null;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(root, loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Probe").Type<PaintCounter>("PaintCounter");
                return modules;
            })
            { WatchFiles = false };
            Assert.True(host.Start());
            loop.Schedule(TimeSpan.FromSeconds(10), () => loop.Quit(1));
            test(loop, platform, host);
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
