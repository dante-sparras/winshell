using SkiaSharp;
using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.Power;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// The on-screen display is a panel on the work area. These tests drive it through a fake platform, so they never
/// move this PC's volume, brightness, theme or taskbar.
/// </summary>
[Collection(ThemeCollection.Name)]
public class OnScreenDisplayTests
{
    private static readonly SystemColors Desert = new(
        Window: QColor.Parse("#fffaef"), WindowText: QColor.Parse("#3d3d3d"), Highlight: QColor.Parse("#903909"),
        HighlightText: QColor.Parse("#fff5e3"), Hotlight: QColor.Parse("#1c5e75"), GrayText: QColor.Parse("#676767"),
        ButtonFace: QColor.Parse("#fffaef"), ButtonText: QColor.Parse("#202020"));

    [Fact]
    public void A_volume_change_after_the_baseline_sits_above_the_work_area()
    {
        using var osd = Flyout.Isolated();
        osd.Set(("animationsEnabled", false), ("volumeKnown", true), ("volume", 0.2));
        osd.Set(("volume", 0.9));
        osd.SettleBaseline();

        osd.Set(("volume", 0.4));
        osd.Layout();

        Assert.Equal("volume", osd.Text("mode"));
        Assert.Equal("Volume 40 percent", osd.Text("announcement"));
        Assert.True(osd.Flag("showLevel"));
        Assert.False(osd.Flag("showMedia"));
        var panel = osd.Panel;
        Assert.Equal("1", panel.Screen!.Id);
        AssertPlaced(panel, 64, new RECT(780, 968, 1140, 1032));
        Assert.Contains("\uE994", Glyphs(osd.Window));
    }

    [Fact]
    public void Mute_volume_and_the_microphone_are_announced_without_a_level_write()
    {
        using var osd = Flyout.Isolated();
        osd.Set(("animationsEnabled", false), ("volumeKnown", true), ("volume", 0.8), ("microphoneKnown", true), ("microphoneMuted", false));
        osd.SettleBaseline();

        osd.Set(("playbackMuted", true));
        osd.Layout();
        Assert.Equal("Volume muted", osd.Text("announcement"));
        Assert.Contains("\uE74F", Glyphs(osd.Window));
        Assert.Equal(0.8, osd.Number("volume"));

        osd.Set(("microphoneMuted", true));
        osd.Layout();
        Assert.Equal("microphone", osd.Text("mode"));
        Assert.Equal("Microphone muted", osd.Text("announcement"));
        Assert.False(osd.Flag("showLevel"));
        Assert.Equal(64, osd.Panel.Height);
        Assert.Contains("\uF781", Glyphs(osd.Window));

        osd.Set(("microphoneMuted", false));
        Assert.Equal("Microphone on", osd.Text("announcement"));
        Assert.Contains("\uE720", Glyphs(osd.Window));
    }

    [Fact]
    public void Brightness_arriving_after_startup_baselines_then_shows_on_that_monitor()
    {
        using var osd = Flyout.Isolated();
        osd.Set(("animationsEnabled", false));
        osd.SettleBaseline();

        osd.Set(("brightnessScreenId", "DISPLAY2"), ("brightnessMinimum", 0), ("brightnessMaximum", 50), ("brightness", 10));
        osd.Set(("brightnessKnown", true));
        osd.Layout();
        Assert.False(osd.Window.Visible);

        osd.Set(("brightness", 25));
        osd.Layout();
        Assert.Equal("brightness", osd.Text("mode"));
        Assert.Equal("Brightness 50 percent", osd.Text("announcement"));
        Assert.Equal("2", osd.Panel.Screen!.Id);
        Assert.Contains("\uE706", Glyphs(osd.Window));
        AssertPlaced(osd.Panel, 64, new RECT(3570, 2052, 4110, 2148));

        osd.Set(("brightnessScreenId", @"\\.\DISPLAY2"));
        osd.Set(("brightness", 40));
        Assert.Equal("2", osd.Panel.Screen!.Id);
        Assert.Equal("Brightness 80 percent", osd.Text("announcement"));

        osd.Hide();
        osd.Set(("brightnessScreenId", "Monitor B"), ("brightness", 10));
        Assert.Equal("2", osd.Panel.Screen!.Id);
    }

    [Fact]
    public void Media_stays_longer_and_grows_when_the_level_is_known()
    {
        using var osd = Flyout.Isolated();
        osd.Set(("animationsEnabled", false), ("mediaKnown", true), ("mediaTitle", "Song"), ("mediaArtist", "Artist"), ("mediaPlaying", false));
        osd.SettleBaseline();

        osd.Set(("mediaPlaying", true));
        osd.Layout();
        Assert.Equal("media", osd.Text("mode"));
        Assert.Equal("Playing Song, Artist", osd.Text("announcement"));
        Assert.True(osd.Flag("showMedia"));
        Assert.False(osd.Flag("showLevel"));
        Assert.Equal(80, osd.Panel.Height);
        AssertPlaced(osd.Panel, 80, new RECT(780, 952, 1140, 1032));
        Assert.Contains("\uE8D6", Glyphs(osd.Window));

        osd.Set(("volumeKnown", true), ("volume", 0.4));
        osd.Layout();
        Assert.Equal("volume", osd.Text("mode"));
        Assert.Equal("Volume 40 percent. Playing Song, Artist", osd.Text("announcement"));
        Assert.True(osd.Flag("showMedia"));
        Assert.Equal(120, osd.Panel.Height);
        AssertPlaced(osd.Panel, 120, new RECT(780, 912, 1140, 1032));
    }

    [Fact]
    public void The_level_hides_after_three_seconds_unless_the_pointer_is_inside()
    {
        using var osd = Flyout.Isolated();
        osd.Set(("animationsEnabled", false), ("volumeKnown", true), ("volume", 0.2));
        osd.SettleBaseline();
        osd.Set(("volume", 0.4));
        osd.Layout();

        osd.Loop.Advance(TimeSpan.FromMilliseconds(2000));
        osd.Set(("volume", 0.5));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(osd.Window.Visible);
        osd.Loop.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(osd.Window.Visible);

        osd.Set(("volume", 0.6));
        osd.Layout();
        osd.Point(180, 32);
        Assert.True(osd.Flag("pointerInside"));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(5000));
        Assert.True(osd.Window.Visible);
        osd.Point(-10, -10);
        Assert.False(osd.Flag("pointerInside"));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(osd.Window.Visible);
        osd.Loop.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(osd.Window.Visible);
    }

    [Fact]
    public void Media_hides_after_five_seconds_and_motion_fades_the_level()
    {
        using var osd = Flyout.Isolated();
        osd.Set(("animationsEnabled", false), ("mediaKnown", true), ("mediaTitle", "Song"), ("mediaPlaying", false));
        osd.SettleBaseline();
        osd.Set(("mediaPlaying", true));
        Assert.Equal("Playing Song", osd.Text("announcement"));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.True(osd.Window.Visible);
        osd.Loop.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(osd.Window.Visible);

        osd.Set(("animationsEnabled", true), ("volumeKnown", true), ("volume", 0.2));
        osd.Set(("volume", 0.3));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(3000));
        Assert.True(osd.Window.Visible);
        osd.Point(180, 32);
        Assert.False(osd.Flag("hiding"));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(osd.Window.Visible);
        osd.Point(-10, -10);
        Assert.False(osd.Flag("pointerInside"));
        Assert.Equal("volume", osd.Text("mode"));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(3000));
        osd.Loop.Advance(TimeSpan.FromMilliseconds(200));
        Assert.False(osd.Window.Visible);
    }

    [Fact]
    public void Transport_buttons_are_the_only_press_that_leaves_the_panel()
    {
        using var osd = Flyout.Isolated();
        osd.Set(
            ("animationsEnabled", false), ("volumeKnown", true), ("volume", 0.4),
            ("mediaKnown", true), ("mediaTitle", "Song"), ("mediaArtist", "Artist"), ("mediaPlaying", false),
            ("canTogglePlaying", true), ("canGoNext", true), ("canGoPrevious", false));
        osd.SettleBaseline();
        osd.Set(("mediaPlaying", true));
        osd.Layout();

        var toggles = 0;
        var previous = 0;
        osd.Window.Connect("togglePlayback", _ => toggles++);
        osd.Window.Connect("previousTrack", _ => previous++);
        osd.Click("previous");
        Assert.Equal(0, previous);
        osd.Click("play");
        Assert.Equal(1, toggles);

        var tree = new AccessibleTree(osd.Window.Scene);
        tree.Refresh();
        var nodes = Flatten(tree.Root).ToList();
        Assert.Contains(nodes, n => n.Role == AccessibleAttached.RoleValue.Notification && n.Name == "Playing Song, Artist");
        var level = Assert.Single(nodes, n => n.Role == AccessibleAttached.RoleValue.Slider);
        Assert.Equal("Volume", level.Name);
        Assert.NotNull(level.Range);
        Assert.InRange(level.Range!.Value, 0.39, 0.41);
        var play = Assert.Single(nodes, n => n.Role == AccessibleAttached.RoleValue.Button && n.Name == "Pause");
        Assert.True(play.CanInvoke);
        play.Invoke();
        Assert.Equal(2, toggles);

        var before = osd.Number("volume");
        osd.Click("level");
        Assert.Equal(before, osd.Number("volume"));
        Assert.InRange(level.Range!.Value, 0.39, 0.41);
    }

    [Fact]
    public void Accent_fills_the_level_and_contrast_drops_acrylic_for_the_system_window()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(false);
            using var osd = Flyout.Isolated();
            osd.Set(("animationsEnabled", false), ("contrast", false), ("volumeKnown", true), ("volume", 0.2));
            osd.SettleBaseline();
            osd.Set(("volume", 0.75));
            osd.Layout();

            Assert.Equal(WindowBackdrop.Acrylic, osd.Backend.Backdrop);
            Assert.Equal(WindowCorners.Round, osd.Backend.Corners);
            Assert.Equal(1, BorderWidth(osd));
            using (var bitmap = osd.Backend.Render())
                Assert.True(Contains(bitmap, Theme.Default(false).AccentFillColorDefault), "the level should use the accent fill");

            Theme.Current = Theme.Default(false) with { System = Desert, IsHighContrast = true };
            osd.Set(("contrast", true));
            osd.Layout();
            Assert.Equal(WindowBackdrop.None, osd.Backend.Backdrop);
            Assert.Equal(2, BorderWidth(osd));
            using var contrast = osd.Backend.Render();
            Assert.True(Contains(contrast, Desert.Window), "a contrast theme paints the system window color");
            Assert.True(Contains(contrast, Desert.Highlight), "a contrast theme paints the highlight on the level");
            Assert.True(Contains(contrast, Desert.WindowText), "a contrast theme paints the window text on the border");
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void The_default_shell_shows_the_panel_from_its_services_and_does_not_reserve_space()
    {
        var services = new FakeServices();
        var speakers = new FakeAudioNode("Speakers");
        speakers.Audio.Volume = 0.2;
        services.Audio.DefaultPlaybackDevice = speakers;
        var mic = new FakeAudioNode("Microphone", isPlayback: false);
        services.Audio.DefaultRecordingDevice = mic;
        using var shell = Flyout.Shell(services);
        shell.SettleBaseline();
        Assert.Equal(0, shell.Panel.ExclusiveZone);
        Assert.Equal(0, shell.Panel.ExclusionMode);
        Assert.Equal(Edges.Bottom, shell.Panel.Anchors);
        Assert.False(shell.Panel.Focusable);
        Assert.True(shell.Panel.AboveWindows);

        speakers.Audio.Volume = 0.4;
        shell.Layout();
        Assert.True(shell.Window.Visible);
        Assert.Equal("Volume 40 percent", shell.Text("announcement"));
        AssertPlaced(shell.Panel, 64, new RECT(780, 968, 1140, 1032));
        shell.Click("level");
        Assert.Equal(0.4, speakers.Audio.Volume);

        shell.Hide();
        mic.Audio.Muted = true;
        shell.Layout();
        Assert.Equal("Microphone muted", shell.Text("announcement"));
        Assert.True(mic.Audio.Muted);
    }

    [Fact]
    public void A_monitor_that_appears_later_does_not_show_until_its_brightness_changes()
    {
        var services = new FakeServices();
        using var shell = Flyout.Services(services);
        shell.SettleBaseline();
        var monitor = new FakeBrightnessMonitor(BrightnessConnection.Internal, 0, 100, "DISPLAY2", "Monitor B") { Brightness = 40 };
        services.Brightness.Monitors.Add(monitor);
        shell.Layout();
        Assert.False(shell.Window.Visible);

        monitor.Brightness = 55;
        shell.Layout();
        Assert.Equal("brightness", shell.Text("mode"));
        Assert.Equal("Brightness 55 percent", shell.Text("announcement"));
        Assert.Equal("2", shell.Panel.Screen!.Id);
        AssertPlaced(shell.Panel, 64, new RECT(3570, 2052, 4110, 2148));
        shell.Click("level");
        Assert.Equal(55, monitor.Brightness);
    }

    [Fact]
    public void Each_monitor_shows_its_own_change_even_at_the_level_another_one_has()
    {
        var services = new FakeServices();
        var first = new FakeBrightnessMonitor(BrightnessConnection.External, 0, 100, "DISPLAY1", "Monitor A") { Brightness = 30 };
        var second = new FakeBrightnessMonitor(BrightnessConnection.Internal, 0, 100, "DISPLAY2", "Monitor B") { Brightness = 70 };
        services.Brightness.Monitors.Add(first);
        services.Brightness.Monitors.Add(second);
        using var shell = Flyout.Services(services);
        shell.SettleBaseline();
        Assert.False(shell.Window.Visible);

        second.Brightness = 55;
        shell.Layout();
        Assert.Equal("Brightness 55 percent", shell.Text("announcement"));
        Assert.Equal("2", shell.Panel.Screen!.Id);

        shell.Hide();
        first.Brightness = 55;
        shell.Layout();
        Assert.True(shell.Window.Visible);
        Assert.Equal("Brightness 55 percent", shell.Text("announcement"));
        Assert.Equal("1", shell.Panel.Screen!.Id);

        // A third monitor that appears is a baseline, not a change.
        shell.Hide();
        services.Brightness.Monitors.Add(new FakeBrightnessMonitor(BrightnessConnection.External, 0, 100, "DISPLAY2", "Monitor C") { Brightness = 10 });
        shell.Layout();
        Assert.False(shell.Window.Visible);
    }

    [Fact]
    public void A_playing_session_shows_the_track_and_its_controls_call_the_session()
    {
        var services = new FakeServices();
        using var shell = Flyout.Services(services);
        shell.SettleBaseline();
        var session = new FakeMediaSession("Player", "Song", "Artist");
        services.Media.Sessions.Add(session);
        shell.Layout();
        Assert.False(shell.Window.Visible);

        session.IsPlaying = true;
        shell.Layout();
        Assert.Equal("media", shell.Text("mode"));
        Assert.Equal("Playing Song, Artist", shell.Text("announcement"));
        Assert.Equal(80, shell.Panel.Height);
        shell.Click("play");
        Assert.Equal(["togglePlaying"], session.Calls);
        shell.Click("previous");
        Assert.DoesNotContain("previous", session.Calls);
        Assert.False(session.IsPlaying);
    }

    private static void AssertPlaced(PanelPlacement panel, int height, RECT physical)
    {
        Assert.Equal(360, panel.Width);
        Assert.Equal(height, panel.Height);
        Assert.Equal(8, panel.MarginBottom);
        Assert.Equal(0, panel.MarginTop);
        Assert.Equal(0, panel.MarginLeft);
        Assert.Equal(0, panel.MarginRight);
        Assert.Equal(Edges.Bottom, panel.Anchors);
        Assert.Equal(0, panel.ExclusionMode);
        Assert.Equal(0, panel.ExclusiveZone);
        Assert.True(panel.AboveWindows);
        Assert.False(panel.Focusable);
        Assert.Equal(physical, Place(panel));
        Assert.Equal(physical.Bottom, PhysicalWorkArea(panel.Screen!).Bottom - (int)Math.Round(8 * panel.Screen!.DevicePixelRatio));
    }

    private static RECT Place(PanelPlacement panel) =>
        DesktopSpace.Anchor(PhysicalWorkArea(panel.Screen!), panel, panel.Screen!.DevicePixelRatio);

    private static RECT PhysicalWorkArea(ScreenInfo screen)
    {
        var scale = screen.DevicePixelRatio;
        var area = screen.Geometry;
        var monitor = new RECT(
            (int)Math.Round(area.X),
            (int)Math.Round(area.Y),
            (int)Math.Round(area.X + area.Width * scale),
            (int)Math.Round(area.Y + area.Height * scale));
        return DesktopSpace.ToPhysical(screen.WorkArea, monitor, scale);
    }

    private static double BorderWidth(Flyout osd) => ((BorderGroup)osd.Find("card").ReadProperty("border")!).Width;

    private static IEnumerable<string> Glyphs(PanelWindow window) =>
        Walk(window.ContentItem).OfType<Text>().Where(t => t.Visible).Select(t => (string)t.ReadProperty("text")!);

    private static bool Contains(SKBitmap bitmap, QColor color)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 200)
                    continue;
                if (Math.Abs(pixel.Red - color.R8) <= 8 && Math.Abs(pixel.Green - color.G8) <= 8 && Math.Abs(pixel.Blue - color.B8) <= 8)
                    return true;
            }
        }
        return false;
    }

    private static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static IEnumerable<Item> Walk(Item item)
    {
        yield return item;
        foreach (var child in item.ChildItems)
        {
            foreach (var found in Walk(child))
                yield return found;
        }
    }

    /// <summary>One loaded on-screen display. The platform's screens match the shell tests: a 100% primary with a taskbar, and a 150% monitor beside it.</summary>
    private sealed class Flyout : IDisposable
    {
        private readonly string _directory;
        private readonly Theme? _theme;

        private Flyout(string directory, Func<ModuleRegistry> modules, Theme? theme)
        {
            _directory = directory;
            _theme = theme;
            Platform = new RecordingPlatform();
            Loop = new ManualEventLoop();
            var log = new Log { Sink = (_, category, message) => Messages.Add($"{category}: {message}"), MinimumLevel = LogLevel.Warn };
            Host = new ShellHost(Path.Combine(directory, "shell.qml"), Loop, Platform, log, modules);
            Host.WatchFiles = false;
            Assert.True(Host.Start(), string.Join(Environment.NewLine, Messages));
            Layout();
        }

        public RecordingPlatform Platform { get; }

        public ManualEventLoop Loop { get; }

        public ShellHost Host { get; }

        public List<string> Messages { get; } = [];

        public PanelWindow Window => Platform.Windows.Select(w => w.Client).OfType<PanelWindow>().Single(p => p.ObjectName == "osd");

        public RecordingWindow Backend => (RecordingWindow)Window.Backend!;

        public PanelPlacement Panel => Backend.Panel ?? throw new InvalidOperationException("The on-screen display was not placed.");

        public static Flyout Isolated()
        {
            var directory = Temp();
            File.Copy(Path.Combine(Repo(), "src", "Winshell", "DefaultShell", "OnScreenDisplay.qml"), Path.Combine(directory, "shell.qml"));
            // Its material follows the shell's preferences.
            File.Copy(Path.Combine(Repo(), "src", "Winshell", "DefaultShell", "Preferences.qml"), Path.Combine(directory, "Preferences.qml"));
            return new Flyout(directory, () =>
            {
                var registry = StandardModules.Create();
                WindowsModules.Register(registry);
                return registry;
            }, null);
        }

        public static Flyout Services(FakeServices services) => Open(services, hostOnly: true);

        public static Flyout Shell(FakeServices services) => Open(services, hostOnly: false);

        private static Flyout Open(FakeServices services, bool hostOnly)
        {
            var theme = Theme.Current;
            Theme.Current = theme with { AnimationsEnabled = false };
            var directory = Temp();
            var source = Path.Combine(Repo(), "src", "Winshell", "DefaultShell");
            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                var target = Path.Combine(directory, name);
                if (name == "shell.qml")
                {
                    var text = File.ReadAllText(file).Replace("winshell-default", "winshell-osd-" + Guid.NewGuid().ToString("N"), StringComparison.Ordinal);
                    File.WriteAllText(target, text);
                }
                else
                {
                    File.Copy(file, target);
                }
            }
            if (hostOnly)
            {
                File.WriteAllText(Path.Combine(directory, "shell.qml"), """
                    import Winshell
                    OnScreenDisplayHost {}
                    """);
            }
            return new Flyout(directory, () =>
            {
                var registry = StandardModules.Create();
                WindowsModules.Register(registry);
                ServiceModules.Register(registry);
                services.Register(registry);
                return registry;
            }, theme);
        }

        public void Set(params (string Name, object? Value)[] values)
        {
            foreach (var (name, value) in values)
                Window.WriteProperty(name, value);
            Loop.Process();
        }

        public void SettleBaseline()
        {
            Loop.Advance(TimeSpan.FromMilliseconds(400));
            Layout();
            Assert.False(Window.Visible);
            var scene = Messages.Where(m => m.Contains("Error", StringComparison.Ordinal) || m.StartsWith("scene:", StringComparison.Ordinal)).ToList();
            Assert.True(scene.Count == 0, string.Join(Environment.NewLine, scene));
        }

        public void Layout()
        {
            Loop.Process();
            PolishQueue.Flush();
            Loop.Process();
        }

        public void Hide()
        {
            Window.Scene.PointerMove(-10, -10);
            Loop.Advance(TimeSpan.FromMilliseconds(6000));
            Assert.False(Window.Visible);
        }

        public void Point(double x, double y)
        {
            Window.Scene.PointerMove(x, y);
            Layout();
        }

        public void Click(string name)
        {
            var item = Find(name);
            Assert.True(item.Width > 0 && item.Height > 0, $"{name} was not laid out");
            var at = item.MapToScene(item.Width / 2, item.Height / 2);
            Window.Scene.PointerPress(at.X, at.Y, QtEnums.LeftButton);
            Window.Scene.PointerRelease(at.X, at.Y, QtEnums.LeftButton);
            Layout();
        }

        public Item Find(string name) =>
            Walk(Window.ContentItem).FirstOrDefault(i => i.ObjectName == name)
            ?? throw new InvalidOperationException($"No item is named {name}.");

        public string Text(string name) => (string)Window.ReadProperty(name)!;

        public bool Flag(string name) => (bool)Window.ReadProperty(name)!;

        public double Number(string name) => Convert.ToDouble(Window.ReadProperty(name), System.Globalization.CultureInfo.InvariantCulture);

        public void Dispose()
        {
            var data = Host.DataDir;
            Host.Dispose();
            if (_theme is { } theme)
                Theme.Current = theme;
            TryDelete(_directory);
            TryDelete(data);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (IOException)
            {
            }
        }

        private static string Temp()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winshell-osd-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string Repo()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("winshell.slnx was not found above the test directory.");
        }
    }
}
