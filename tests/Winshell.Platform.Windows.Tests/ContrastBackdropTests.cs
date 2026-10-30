using SkiaSharp;
using Winshell.Platform.Windows.Tests.DefaultShell;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

[Collection(ThemeCollection.Name)]
public class ContrastBackdropTests
{
    [Fact]
    public void A_contrast_theme_drops_the_backdrop_and_fills_a_transparent_window()
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
                  objectName: "flyout"
                  implicitWidth: 40
                  implicitHeight: 20
                  color: "transparent"
                  WindowEffects.backdrop: WindowEffects.Acrylic
                  property int shown: WindowEffects.backdrop
                }
                """);
            Theme.Current = Theme.Default(false) with { IsHighContrast = true };
            host = new ShellHost(path, loop, platform, new Log(), () => registry) { WatchFiles = false };
            host.Start();
            loop.Process();
            var backend = platform.Windows.Single();
            var window = (ShellWindow)backend.Client;
            Assert.Equal(WindowBackdrop.None, backend.Backdrop);
            Assert.Equal(0, window.ReadProperty("shown"));
            using var bitmap = new SKBitmap(1, 1);
            using (var canvas = new SKCanvas(bitmap))
                window.OnPaint(canvas);
            var solid = Theme.Current.SolidBackgroundFillColorBase;
            Assert.Equal(new SKColor(solid.R8, solid.G8, solid.B8, solid.A8), bitmap.GetPixel(0, 0));

            Theme.Current = Theme.Default(false);
            loop.Process();
            Assert.Equal(WindowBackdrop.Acrylic, backend.Backdrop);
            Assert.Equal(2, window.ReadProperty("shown"));
        }
        finally
        {
            host?.Dispose();
            Theme.Current = saved;
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
