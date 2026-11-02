using System.Diagnostics;
using Windows.Graphics.Capture;
using Winshell.Platform.Windows.Services.Capture;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Opt-in desktop test: captures only its own window to temp; monitor frames stay in memory.</summary>
[Collection(ThemeCollection.Name)]
public class NativeCaptureTests(ITestOutputHelper output)
{
    [Fact]
    public void Windows_capture_reads_own_window_resizes_and_monitor_then_releases_resources()
    {
        if (Environment.GetEnvironmentVariable("WINSHELL_TEST_CAPTURE") != "1")
        {
            output.WriteLine("Native capture not requested. Set WINSHELL_TEST_CAPTURE=1 on an interactive Windows desktop.");
            return;
        }
        Assert.True(GraphicsCaptureSession.IsSupported());
        var directory = Path.Combine(Path.GetTempPath(), "winshell-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "shell.qml");
        File.WriteAllText(path, """
            import Winshell
            import QtQuick
            ShellRoot {
              PanelWindow {
                anchors { top: true; left: true }
                exclusionMode: ExclusionMode.Ignore
                implicitWidth: 320
                implicitHeight: 180
                color: "#ff0000"
              }
            }
            """);
        try
        {
            using var loop = new WinEventLoop();
            var log = new Log();
            using var platform = new WindowsPlatform(loop, log);
            using var host = new ShellHost(path, loop, platform, log) { WatchFiles = false };
            Assert.True(host.Start());
            var window = Assert.Single(platform.Windows);
            Assert.True(window.IsVisible);
            using var capture = new WindowsCaptureBackend();
            loop.Schedule(TimeSpan.FromMilliseconds(1000), () => capture.Start(new CaptureTarget(window.Handle, ""), false, () => { }));
            var clock = Stopwatch.StartNew();
            var stage = 0;
            var initialWidth = 0;
            Exception? failure = null;
            WindowsCaptureBackend? monitor = null;
            CaptureSession? owned = null;
            var reloads = 0;
            void StartOwned()
            {
                var target = new AppWindow(null!, Assert.Single(platform.Windows).Handle);
                owned = new CaptureSession { Engine = host.Engine, Target = target };
                target.SetOwner(owned);
                owned.SetOwner(host.Root);
                Assert.True(owned.Start(), owned.Error);
            }
            void Poll()
            {
                try
                {
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"Capture timed out at stage {stage}.");
                    if (stage == 3)
                    {
                        Assert.NotNull(owned);
                        Assert.Empty(owned.Error);
                        if (owned.HasFrame)
                        {
                            var retired = owned;
                            host.Reload(hard: false);
                            Assert.True(retired.IsDestroyed);
                            Assert.False(retired.HasFrame);
                            Assert.False(retired.Running);
                            reloads++;
                            if (reloads == 4)
                            {
                                output.WriteLine("Four live capture generations released on reload.");
                                loop.Quit(0);
                                return;
                            }
                            StartOwned();
                        }
                        loop.Schedule(TimeSpan.FromMilliseconds(33), Poll);
                        return;
                    }
                    var frame = (monitor ?? capture).ReadLatest();
                    if (frame is not null)
                    {
                        output.WriteLine($"stage {stage}: {frame.Width}x{frame.Height} physical pixels");
                        if (stage == 0)
                        {
                            var center = (frame.Height / 2 * frame.Width + frame.Width / 2) * 4;
                            Assert.Equal(new byte[] { 0, 0, 255, 255 }, frame.Bgra.Slice(center, 4).ToArray());
                            File.WriteAllBytes(Path.Combine(directory, "own-window.png"), frame.EncodePng());
                            initialWidth = frame.Width;
                            window.SetPanel(new PanelPlacement(null, Edges.Top | Edges.Left, 0, 0, 0, 0,
                                480, 240, 1, 0, true, false, "capture-test"));
                            stage = 1;
                        }
                        else if (stage == 1 && frame.Width > initialWidth)
                        {
                            capture.Dispose();
                            monitor = new WindowsCaptureBackend();
                            monitor.Start(new CaptureTarget(0, platform.Screens[0].Id), false, () => { });
                            stage = 2;
                        }
                        else if (stage == 2)
                        {
                            var screen = platform.Screens[0];
                            Assert.Equal((int)Math.Round(screen.Geometry.Width * screen.DevicePixelRatio), frame.Width);
                            Assert.Equal((int)Math.Round(screen.Geometry.Height * screen.DevicePixelRatio), frame.Height);
                            monitor!.Dispose();
                            monitor = null;
                            stage = 3;
                            StartOwned();
                        }
                    }
                    loop.Schedule(TimeSpan.FromMilliseconds(33), Poll);
                }
                catch (Exception e)
                {
                    failure = e;
                    loop.Quit(1);
                }
            }
            loop.Schedule(TimeSpan.FromMilliseconds(1200), Poll);
            try { loop.Run(); }
            finally { monitor?.Dispose(); }
            Assert.Null(failure);
            Assert.Equal(3, stage);
            Assert.Equal(4, reloads);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
