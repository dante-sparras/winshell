using SkiaSharp;
using Winshell.Platform.Windows.Tests.DefaultShell;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

[Collection(ThemeCollection.Name)]
public class ContrastTransparentWindowTests
{
    [Fact]
    public void A_contrast_theme_keeps_a_window_without_a_backdrop_transparent()
    {
        var saved = Theme.Current;
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        var loop = new ManualEventLoop();
        var platform = new RecordingPlatform();
        ShellHost? host = null;
        try
        {
            var path = Path.Combine(directory, "shell.qml");
            File.WriteAllText(path, """
                import Winshell
                PopupWindow {
                  implicitWidth: 40
                  implicitHeight: 20
                  color: "transparent"
                  WindowEffects.backdrop: WindowEffects.None
                }
                """);
            Theme.Current = Theme.Default(false) with { IsHighContrast = true };
            host = new ShellHost(path, loop, platform, new Log(), () => registry) { WatchFiles = false };
            host.Start();
            loop.Process();
            var backend = platform.Windows.Single();
            Assert.Equal(WindowBackdrop.None, backend.Backdrop);
            using var bitmap = new SKBitmap(1, 1);
            using (var canvas = new SKCanvas(bitmap))
                backend.Client.OnPaint(canvas);
            Assert.Equal((byte)0, bitmap.GetPixel(0, 0).Alpha);
        }
        finally
        {
            host?.Dispose();
            Theme.Current = saved;
            Directory.Delete(directory, true);
        }
    }
}
