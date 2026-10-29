using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Windows text size multiplies the type ramp and text that has no size of its own.</summary>
[Collection(ThemeCollection.Name)]
public class TextScaleTests
{
    [Fact]
    public void An_unusable_text_scale_is_the_default()
    {
        Assert.Equal(1, Theme.TextScale(0));
        Assert.Equal(1, Theme.TextScale(-2));
        Assert.Equal(1, Theme.TextScale(double.NaN));
        Assert.Equal(1, Theme.TextScale(double.PositiveInfinity));
        Assert.Equal(1.5, Theme.TextScale(1.5));
    }

    [Fact]
    public void Text_size_scales_the_type_ramp_and_leaves_an_explicit_size()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "shell.qml"), """
            import QtQuick
            import Winshell.Theme
            Item {
              property real scale: Theme.textScaleFactor
              property real body: Theme.typography.body.pixelSize
              property real plain: plainText.font.pixelSize
              property real pinned: pinnedText.font.pixelSize
              property real pointed: pointedText.font.pixelSize
              property real plainHeight: plainText.implicitHeight
              Text { id: plainText; text: "Ag" }
              Text { id: pinnedText; text: "Ag"; font.pixelSize: 14 }
              Text { id: pointedText; text: "Ag"; font.pointSize: 12 }
            }
            """);
        var saved = Theme.Current;
        var savedSize = FontSpec.DefaultPixelSize;
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(registry, loop, new Log(), directory);
        try
        {
            Theme.Current = Theme.Default(false) with { TextScaleFactor = 2 };
            var root = engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            loop.Process();

            Assert.Equal(2.0, root.ReadProperty("scale"));
            Assert.Equal(28.0, root.ReadProperty("body"));
            Assert.Equal(28.0, root.ReadProperty("plain"));
            Assert.Equal(14.0, root.ReadProperty("pinned"));
            Assert.Equal(16.0, root.ReadProperty("pointed"));
            var tall = (double)root.ReadProperty("plainHeight")!;

            Theme.Current = Theme.Default(false) with { TextScaleFactor = 1 };
            loop.Process();
            Assert.Equal(1.0, root.ReadProperty("scale"));
            Assert.Equal(14.0, root.ReadProperty("body"));
            Assert.Equal(14.0, root.ReadProperty("plain"));
            Assert.Equal(14.0, root.ReadProperty("pinned"));
            Assert.Equal(16.0, root.ReadProperty("pointed"));
            Assert.True((double)root.ReadProperty("plainHeight")! < tall);
        }
        finally
        {
            Theme.Current = saved;
            FontSpec.DefaultPixelSize = savedSize;
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
