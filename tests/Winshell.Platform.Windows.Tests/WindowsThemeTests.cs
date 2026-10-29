using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Platform.Windows.Tests;

/// <summary>Tests that change <see cref="Theme.Current"/>, which every test in the process reads.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThemeCollection
{
    public const string Name = "Theme";
}

[Collection(ThemeCollection.Name)]
public class WindowsThemeTests
{
    private static readonly SystemColors Desert = new(
        Window: QColor.Parse("#fffaef"), WindowText: QColor.Parse("#3d3d3d"), Highlight: QColor.Parse("#903909"),
        HighlightText: QColor.Parse("#fff5e3"), Hotlight: QColor.Parse("#1c5e75"), GrayText: QColor.Parse("#676767"),
        ButtonFace: QColor.Parse("#fffaef"), ButtonText: QColor.Parse("#202020"));

    [Fact]
    public void Bindings_to_the_theme_follow_light_dark_and_contrast_without_a_reload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "shell.qml"), """
            import QtQuick
            import Winshell.Theme
            Item {
              property bool dark: Theme.dark
              property bool contrast: Theme.highContrast
              property bool animations: Theme.animationsEnabled
              property color text: Theme.textFillColorPrimary
              property color accent: Theme.accentFillColorDefault
              property color window: Theme.systemColorWindowColor
              property real shadowAlpha: Theme.flyoutShadow.color.a
              property real controlRadius: Theme.controlCornerRadius
              property real overlayRadius: Theme.overlayCornerRadius
              property int fast: Theme.controlFastAnimationDuration
              property var spline: Theme.controlFastOutSlowInKeySpline
              property real titleSize: title.font.pixelSize
              property int titleWeight: title.font.weight
              property real captionSize: Theme.typography.caption.pixelSize
              Text { id: title; font: Theme.typography.title }
            }
            """);
        var saved = Theme.Current;
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(registry, loop, new Log(), directory);
        try
        {
            Theme.Current = Theme.Default(false);
            var root = engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            loop.Process();

            Assert.False((bool)root.ReadProperty("dark")!);
            Assert.True((bool)root.ReadProperty("animations")!);
            Theme.Current = Theme.Current with { AnimationsEnabled = false };
            loop.Process();
            Assert.False((bool)root.ReadProperty("animations")!);
            Theme.Current = Theme.Current with { AnimationsEnabled = true };
            Assert.Equal(QColor.Parse("#e4000000"), root.ReadProperty("text"));
            Assert.Equal(AccentColors.Default.Dark1, root.ReadProperty("accent"));
            Assert.Equal(4.0, root.ReadProperty("controlRadius"));
            Assert.Equal(8.0, root.ReadProperty("overlayRadius"));
            Assert.Equal(167, root.ReadProperty("fast"));
            Assert.Equal(new object?[] { 0.0, 0.0, 0.0, 1.0, 1.0, 1.0 }, ValueConverter.ToList(root.ReadProperty("spline")));
            Assert.Equal(28.0, root.ReadProperty("titleSize"));
            Assert.Equal(600, root.ReadProperty("titleWeight"));
            Assert.Equal(12.0, root.ReadProperty("captionSize"));
            var lightShadow = (double)root.ReadProperty("shadowAlpha")!;

            Theme.Current = Theme.Default(true);
            loop.Process();
            Assert.True((bool)root.ReadProperty("dark")!);
            Assert.Equal(QColor.Parse("#ffffff"), root.ReadProperty("text"));
            Assert.Equal(AccentColors.Default.Light2, root.ReadProperty("accent"));
            Assert.True((double)root.ReadProperty("shadowAlpha")! > lightShadow);

            Theme.Current = Theme.Default(false) with { System = Desert, IsHighContrast = true };
            loop.Process();
            Assert.True((bool)root.ReadProperty("contrast")!);
            Assert.Equal(Desert.WindowText, root.ReadProperty("text"));
            Assert.Equal(Desert.Window, root.ReadProperty("accent"));
            Assert.Equal(Desert.Window, root.ReadProperty("window"));
            Assert.Equal(0.0, root.ReadProperty("shadowAlpha"));
        }
        finally
        {
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

    [Fact]
    public void Theme_tokens_are_read_only()
    {
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "shell.qml"), """
            import QtQuick
            import Winshell.Theme
            Item {
              property color text: Theme.textFillColorPrimary
              Component.onCompleted: Theme.textFillColorPrimary = "red"
            }
            """);
        var messages = new List<string>();
        var log = new Log { Sink = (level, category, message) => messages.Add(message) };
        using var engine = new QmlEngine(registry, new ManualEventLoop(), log, directory);
        var root = engine.CreateRoot(Path.Combine(directory, "shell.qml"));
        Assert.Contains(messages, m => m.Contains("read-only property \"textFillColorPrimary\"", StringComparison.Ordinal));
        Assert.Equal(Theme.Current.TextFillColorPrimary, root.ReadProperty("text"));
        Directory.Delete(directory, true);
    }
}
