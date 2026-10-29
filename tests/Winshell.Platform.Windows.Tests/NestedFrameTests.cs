using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A real window that resizes itself while it draws, as a window sized to its content does when drawing lays the
/// content out. Windows answers the resize with WM_SIZE inside the frame; a frame must never start there, since it
/// would replace the buffer the first one is drawing into. The window draws again at its new size afterwards.
/// </summary>
public class NestedFrameTests
{
    /// <summary>Records how deep paints nest, and resizes its window from inside one paint when asked.</summary>
    public sealed class Resizer : Item
    {
        public static int Depth;
        public static int MaxDepth;
        public static ShellWindow? Resize;
        public static IEventLoop? Loop;
        public static List<double>? SeenAfterTheFrame;
        public static List<double> PaintedHeights { get; } = [];

        public override void Paint(SKCanvas canvas, PaintContext context)
        {
            Depth++;
            MaxDepth = Math.Max(MaxDepth, Depth);
            try
            {
                PaintedHeights.Add(Height);
                if (Interlocked.Exchange(ref Resize, null) is { } window)
                {
                    window.ImplicitHeight = 80;
                    // The first thing the loop runs after this frame sees what the frame drew.
                    Loop?.Post(() => SeenAfterTheFrame = [.. PaintedHeights]);
                }
                base.Paint(canvas, context);
            }
            finally
            {
                Depth--;
            }
        }
    }

    private const string Config = """
        import Winshell
        import Probe
        ShellRoot {
          PanelWindow {
            anchors { top: true; left: true }
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 120
            implicitHeight: 40
            Resizer { anchors.fill: parent }
          }
        }
        """;

    [Fact]
    public void A_window_that_resizes_while_it_draws_draws_again_after_the_frame()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, Config);
        var log = new Log { Sink = (_, _, _) => { } };
        Resizer.Depth = 0;
        Resizer.MaxDepth = 0;
        Resizer.PaintedHeights.Clear();
        Resizer.SeenAfterTheFrame = null;
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(root, loop, platform, log, () =>
            {
                var modules = StandardModules.Create();
                modules.Module("Probe").Type<Resizer>("Resizer");
                return modules;
            })
            { WatchFiles = false };
            Assert.True(host.Start());
            var window = (ShellWindow)Assert.Single(platform.Windows).Client;
            Resizer.Loop = loop;

            loop.Schedule(TimeSpan.FromMilliseconds(100), () =>
            {
                Resizer.Resize = window;
                window.Scene.RequestUpdate();
            });
            loop.Schedule(TimeSpan.FromMilliseconds(600), () => loop.Quit(0));
            loop.Run();

            Assert.Null(Resizer.Resize);
            Assert.Equal(1, Resizer.MaxDepth);
            Assert.Equal(80, Resizer.PaintedHeights[^1]);
            // The new size is drawn in the same frame, so the window never shows its old size at its new one.
            Assert.Equal(80, Resizer.SeenAfterTheFrame![^1]);
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
