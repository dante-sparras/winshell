using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell;
using Winshell.DefaultShell.Taskbar;
using Winshell.Input;
using Winshell.Native;

// One shell, one dispatcher. A menu left open while this test yields, or a log sink that sees another test's
// crash, fails a check that is about Settings alone.
[assembly: global::Xunit.v3.Parallelization(Mode = global::Xunit.Sdk.ParallelMode.None)]

namespace Winshell.ShellTests;

/// <summary>
/// Winshell Settings on the test shell. The window is the real one <see cref="SettingsHost"/> keeps. The taskbar's
/// own right-click handler is not raised here: building that view takes the notification area and the Win hotkey.
/// The menu it would show is opened on a real panel instead.
/// </summary>
[Trait("Category", "Slow")]
public sealed class SettingsShellTests
{
    [Fact]
    public async Task The_shell_follows_each_choice_at_once_without_a_reload()
    {
        var followed = await ShellFixture.On(async shell =>
        {
            var scheme = shell.ColorScheme;
            using var rig = new Rig(shell, "taskbarCentered = false\n");
            var messages = rig.Listen();
            try
            {
                Assert.False(rig.Preferences.TaskbarCentered);
                Assert.Same(shell, Shell.Current);
                Winshell.DefaultShell.ShellPreferences.Follow(shell, rig.Preferences);

                Assert.Equal("", shell.Ipc.Call("settings", "open", ["taskbarAutoHide"]));
                var window = await rig.Shown();
                Assert.True(window.Visible);
                Assert.Equal("Winshell Settings", window.Title);
                Assert.Equal("taskbar", Page(window));

                rig.Preferences.TaskbarAutoHide = true;
                rig.Loop.Process();
                Assert.Equal(true, Read(rig.PreferencesPath, "taskbarAutoHide"));

                rig.Preferences.TaskbarAllScreens = false;
                rig.Loop.Process();
                Assert.False(rig.Preferences.TaskbarAllScreens);

                rig.Preferences.Backdrop = SettingsCatalog.Materials[1].Value;
                Assert.Equal(WindowBackdrop.Mica, rig.Preferences.Material);

                rig.Preferences.ColorScheme = SettingsCatalog.ColorSchemes[2].Value;
                Assert.Equal(ColorScheme.Dark, shell.ColorScheme);
                Assert.True(shell.IsDark);
                Assert.Same(shell, Shell.Current);
                return messages.Where(m => !m.StartsWith("hotkey:", StringComparison.Ordinal) && !m.StartsWith("ipc: Duplicate IPC name", StringComparison.Ordinal)).ToArray();
            }
            finally
            {
                shell.ColorScheme = scheme;
                rig.Restore();
            }
        });

        Assert.Empty(followed);
    }

    [Fact]
    public async Task The_config_folder_setting_opens_About_at_its_button()
    {
        var enabled = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            try
            {
                Assert.Equal("", shell.Ipc.Call("settings", "open", ["configFolder"]));

                var window = await rig.Shown();

                Assert.Equal("about", Page(window));
                return Button(window, "Open the config folder").IsEnabled;
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.True(enabled);
    }

    [Fact]
    public async Task A_page_title_starts_where_its_cards_do()
    {
        var (title, card) = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            try
            {
                Assert.Equal("", shell.Ipc.Call("settings", "open", ["personalization"]));
                var window = await rig.Shown();
                var root = (FrameworkElement)window.Content!;
                var title = Find<StackPanel>(root, panel => panel.Name == "Breadcrumb") ?? throw new InvalidOperationException("Settings has no title.");
                var card = Find<SettingsCard>(root, found => found.ActualWidth > 0) ?? throw new InvalidOperationException("Settings shows no card.");
                return (Left(title, root), Left(card, root));
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.Equal(card, title, 0.5);
    }

    [Fact]
    public async Task Every_setting_title_fits_on_one_line_at_the_default_size()
    {
        var wrapped = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            try
            {
                // The settings themselves. A notifications card is titled with an app's name from this PC.
                var settings = SettingsCatalog.Settings.Select(setting => setting.Title).ToHashSet(StringComparer.Ordinal);
                var found = new List<string>();
                foreach (var page in SettingsCatalog.Pages)
                {
                    Assert.Equal("", shell.Ipc.Call("settings", "open", [page.Key]));
                    var root = (FrameworkElement)(await rig.Shown()).Content!;
                    root.UpdateLayout();
                    Assert.Equal(SettingsCatalog.WindowWidth, root.ActualWidth);
                    // Every title has the same style, so one line is the shortest title on the page.
                    var titles = Cards(root).Where(card => settings.Contains(card.Title)).Select(card => (card.Title, Height: TitleHeight(card))).ToList();
                    var line = titles.Min(title => title.Height);
                    found.AddRange(titles.Where(title => title.Height > line + 0.5).Select(title => $"{page.Key}: {title.Title}"));
                }

                return found;
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.Empty(wrapped);
    }

    [Fact]
    public async Task Every_switch_ends_at_the_edge_the_drop_downs_and_buttons_end_at()
    {
        var misaligned = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            try
            {
                var found = new List<string>();
                foreach (var page in SettingsCatalog.Pages)
                {
                    Assert.Equal("", shell.Ipc.Call("settings", "open", [page.Key]));
                    var root = (FrameworkElement)(await rig.Shown()).Content!;
                    root.UpdateLayout();
                    foreach (var card in Cards(root))
                    {
                        if (card.TrailContent is not ToggleSwitch { Visibility: Visibility.Visible } toggle)
                            continue;
                        // A drop-down or a button ends at the right edge of the card's control column.
                        var column = card.FindName("TrailHost") as FrameworkElement ?? throw new InvalidOperationException("A card has no TrailHost.");
                        var edge = Right(column, root);
                        var shown = Words(toggle).Max(word => Right(word, root));
                        if (Math.Abs(edge - shown) > 1)
                            found.Add($"{page.Key}: {card.Title} ends at {shown}, not {edge}");
                    }
                }

                return found;
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.Empty(misaligned);
    }

    [Fact]
    public async Task Automatic_updates_say_why_they_are_off_when_this_build_does_not_update_itself()
    {
        var (available, description) = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            try
            {
                Assert.Equal("", shell.Ipc.Call("settings", "open", ["autoUpdate"]));
                var root = (FrameworkElement)(await rig.Shown()).Content!;
                var title = SettingsCatalog.RequireSetting("autoUpdate").Title;
                var card = Find<SettingsCard>(root, found => found.Title == title) ?? throw new InvalidOperationException("About has no automatic updates card.");
                return (shell.Updates.Available, card.Description);
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.False(available);
        Assert.Equal(SettingsAbout.AutomaticUnavailable, description);
    }

    [Fact]
    public async Task The_taskbar_menu_offers_Taskbar_settings_and_closes_when_it_is_chosen()
    {
        var opened = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            var messages = rig.Listen();
            try
            {
                var bar = new Grid { Width = 400, Height = 48 };
                using var panel = new PanelWindow
                {
                    Anchors = Edges.Bottom | Edges.Left | Edges.Right,
                    Width = 400,
                    Height = 48,
                    Content = bar,
                };
                panel.Visible = true;
                await ShellFixture.LaidOut(bar);

                var chosen = "";
                var menu = TaskMenuFlyout.Create(TaskMenu.BarName, [TaskMenu.Settings], id => chosen = id, 16);
                var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                menu.Opened += (_, _) => shown.TrySetResult();
                // Transient takes no focus. A menu that does closes when another program's window opens, even before Opened.
                menu.ShowAt(bar, new FlyoutShowOptions { Position = new global::Windows.Foundation.Point(200, 24), Placement = FlyoutPlacementMode.Top, ShowMode = FlyoutShowMode.Transient });
                await shown.Task;

                var item = MenuItem(bar);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
                Assert.Equal("Taskbar settings", peer.GetName());
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();

                Assert.Equal("taskbarSettings", chosen);
                Assert.False(menu.IsOpen);
                return messages.Where(m => !m.StartsWith("hotkey:", StringComparison.Ordinal) && !m.StartsWith("ipc: Duplicate IPC name", StringComparison.Ordinal)).ToArray();
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.Empty(opened);
    }

    [Fact]
    public async Task No_heading_repeats_the_title_of_a_card_on_its_page()
    {
        var repeated = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            try
            {
                var settings = SettingsCatalog.Settings.Select(setting => setting.Title).ToHashSet(StringComparer.Ordinal);
                var found = new List<string>();
                foreach (var page in SettingsCatalog.Pages)
                {
                    Assert.Equal("", shell.Ipc.Call("settings", "open", [page.Key]));
                    var root = (FrameworkElement)(await rig.Shown()).Content!;
                    root.UpdateLayout();
                    var pages = Find<Grid>(root, grid => grid.Name == "Pages") ?? throw new InvalidOperationException("Settings has no pages.");
                    // The settings themselves. A notifications card is titled with an app's name from this PC.
                    var titles = Cards(pages).Select(card => card.Title).Where(settings.Contains).ToHashSet(StringComparer.Ordinal);
                    found.AddRange(Headings(pages).Where(titles.Contains).Select(heading => $"{page.Key}: {heading}"));
                }

                return found;
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.Empty(repeated);
    }

    [Fact]
    public async Task Settings_is_built_on_its_first_open_and_closing_it_keeps_it_where_it_was()
    {
        var kept = await ShellFixture.On(async shell =>
        {
            using var rig = new Rig(shell, null);
            var messages = rig.Listen();
            try
            {
                Assert.DoesNotContain(shell.Platform.Windows, window => window.Content is SettingsView);

                Assert.Equal("", shell.Ipc.Call("settings", "open", ["taskbarAutoHide"]));
                var window = await rig.Shown();
                Assert.True(window.Visible);
                Assert.Equal("taskbar", Page(window));
                var host = window.Host;

                Win32.SendMessageW(host!.Handle, Win32.WM_CLOSE, 0, 0);
                Assert.False(window.Visible);

                shell.Ipc.Call("settings", "open", ["about"]);
                await ShellFixture.LaidOut((FrameworkElement)window.Content!);

                Assert.Same(window, SettingsWindow(shell));
                Assert.Same(host, window.Host);
                Assert.Equal("about", Page(window));

                // An open that names no page brings it back where it was.
                Win32.SendMessageW(host.Handle, Win32.WM_CLOSE, 0, 0);
                shell.Ipc.Call("settings", "open", [""]);
                await ShellFixture.LaidOut((FrameworkElement)window.Content!);

                Assert.True(window.Visible);
                Assert.Same(host, window.Host);
                Assert.Equal("about", Page(window));
                Assert.True(SearchFocused(window));
                return messages.Where(m => !m.StartsWith("hotkey:", StringComparison.Ordinal) && !m.StartsWith("ipc: Duplicate IPC name", StringComparison.Ordinal)).ToArray();
            }
            finally
            {
                rig.Restore();
            }
        });

        Assert.Empty(kept);
    }

    private static string Page(FloatingWindow window)
    {
        var nav = Find<NavigationView>((DependencyObject)window.Content!) ?? throw new InvalidOperationException("Settings has no navigation view.");
        return (nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "";
    }

    private static double Left(UIElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(default).X;

    private static double Right(FrameworkElement element, UIElement root) =>
        Left(element, root) + element.ActualWidth;

    /// <summary>The "On" and the "Off" a switch draws beside its knob; it shows one of them.</summary>
    private static IEnumerable<FrameworkElement> Words(ToggleSwitch toggle) =>
        new[] { "OnContentPresenter", "OffContentPresenter" }.Select(name =>
            Find<ContentPresenter>(toggle, presenter => presenter.Name == name) ?? throw new InvalidOperationException($"A switch has no {name}."));

    private static double TitleHeight(SettingsCard card) =>
        (Find<TextBlock>(card, text => text.Text == card.Title) ?? throw new InvalidOperationException($"The card {card.Title} has no title.")).ActualHeight;
    /// <summary>The cards on the page that shows. A collapsed page keeps the sizes it had, so it is skipped by its visibility.</summary>
    private static IEnumerable<SettingsCard> Cards(DependencyObject root)
    {
        if (root is UIElement { Visibility: Visibility.Collapsed })
            yield break;
        if (root is SettingsCard card)
        {
            yield return card;
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var found in Cards(VisualTreeHelper.GetChild(root, i)))
                yield return found;
        }
    }

    /// <summary>The text the page that shows has outside its cards: its headings and its notes.</summary>
    private static IEnumerable<string> Headings(DependencyObject root)
    {
        if (root is UIElement { Visibility: Visibility.Collapsed } or SettingsCard)
            yield break;
        if (root is TextBlock { Text.Length: > 0 } text)
            yield return text.Text;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var found in Headings(VisualTreeHelper.GetChild(root, i)))
                yield return found;
        }
    }

    private static bool SearchFocused(FloatingWindow window)
    {
        var focused = FocusManager.GetFocusedElement(window.Content!.XamlRoot) as DependencyObject;
        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is AutoSuggestBox { Name: "Search" })
                return true;
        }

        return false;
    }

    private static MenuFlyoutItem MenuItem(UIElement root)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
        {
            if (popup.Child is not null && Find<MenuFlyoutItem>(popup.Child) is { } item)
                return item;
        }

        throw new InvalidOperationException("The taskbar menu has no item.");
    }

    private static Button Button(FloatingWindow window, string name)
    {
        var found = Find<Button>((DependencyObject)window.Content!, button => AutomationProperties.GetName(button) == name);
        return found ?? throw new InvalidOperationException($"Settings has no button \"{name}\".");
    }

    private static FloatingWindow SettingsWindow(Shell shell)
    {
        var host = shell.Platform.Windows.Single(window => window.Content is SettingsView);
        return (FloatingWindow)ShellWindow.Of((SettingsView)host.Content!)!;
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        if (root is T hit && (match is null || match(hit)))
            return hit;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = Find(VisualTreeHelper.GetChild(root, i), match);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static object? Read(string path, string key)
    {
        Assert.True(TomlDocument.TryParse(path, File.ReadAllText(path), out var document, out var error), error);
        Assert.True(document.Values.TryGetValue(key, out var value), key);
        return value;
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("winshell-settings-shell-").FullName;
        private readonly Action<LogLevel, string, string>? _sink;
        private readonly List<string> _messages = [];

        public Rig(Shell shell, string? preferences)
        {
            Shell = shell;
            _sink = shell.Log.Sink;
            try
            {
                shell.Log.Sink = (level, category, message) =>
                {
                    _messages.Add($"{category}: {message}");
                    _sink?.Invoke(level, category, message);
                };
                Config = Path.Combine(_directory, "config");
                Directory.CreateDirectory(Config);
                PreferencesPath = Path.Combine(Config, "preferences.toml");
                if (preferences is not null)
                    File.WriteAllText(PreferencesPath, preferences);
                Loop = new ManualEventLoop();
                Preferences = new Preferences(PreferencesPath, Loop, shell.Log);
                Start = new Start(Path.Combine(Config, "launcher.toml"), Loop, shell.Log);
                Layout = new QuickSettingsLayout(Path.Combine(_directory, "quick-settings.toml"), Loop, shell.Log);
                Hotkey = shell.NewHotkey();
                Host = new SettingsHost(shell, Preferences, Start, Layout, Hotkey);
            }
            catch
            {
                shell.Log.Sink = _sink;
                throw;
            }
        }

        public Shell Shell { get; }

        public string Config { get; }

        public string PreferencesPath { get; }

        public ManualEventLoop Loop { get; }

        public Preferences Preferences { get; }

        public Start Start { get; }

        public QuickSettingsLayout Layout { get; }

        public Hotkey Hotkey { get; }

        public SettingsHost Host { get; }

        public List<string> Listen() => _messages;

        public async Task<FloatingWindow> Shown()
        {
            var window = SettingsWindow(Shell);
            if (window.Content is FrameworkElement content)
                await ShellFixture.LaidOut(content);
            return window;
        }

        public void Restore() => Shell.Log.Sink = _sink;

        public void Dispose()
        {
            Restore();
            foreach (var host in Shell.Platform.Windows.Where(window => window.Content is SettingsView).ToList())
            {
                if (host.Content is UIElement view && ShellWindow.Of(view) is { } window)
                    window.Dispose();
                else
                    host.Dispose();
            }

            Hotkey.Dispose();
            Start.Dispose();
            Layout.Dispose();
            Preferences.Dispose();
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
