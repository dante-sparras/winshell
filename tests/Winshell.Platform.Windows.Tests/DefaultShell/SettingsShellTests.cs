using System.Text.Json;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// The whole default shell with Winshell Settings in it, on a fake platform with two screens. Its config folder,
/// data and state are temporary folders, so the user's preferences, pins and tiles are never read or written.
/// </summary>
[Collection(ThemeCollection.Name)]
public class SettingsShellTests
{
    [Fact]
    public void The_shell_follows_each_choice_at_once_without_a_reload()
    {
        using var run = new RunningShell("""{ "taskbarCentered": false }""");
        Assert.Equal(2, run.Taskbars.Length);
        Assert.All(run.Taskbars, t => Assert.False((bool)t.ReadProperty("centered")!));
        var generation = run.Host.Engine;

        Assert.Equal("", run.Host.Ipc.Call("settings", "open", ["taskbarAutoHide"]));
        run.Settle();
        Assert.True(run.Settings.Visible);
        Assert.True(run.Backend(run.Settings).Activations > 0);
        Assert.Equal("taskbar", run.View.ReadProperty("page"));

        run.Click(run.Control<Switch>("taskbarAutoHide"));
        Assert.All(run.Taskbars, t => Assert.True((bool)t.ReadProperty("autoHide")!));
        Assert.True(run.Saved().GetProperty("taskbarAutoHide").GetBoolean());

        // Off: the main screen keeps its taskbar, the other loses its own.
        run.Click(run.Control<Switch>("taskbarAllScreens"));
        Assert.Equal("1", Assert.Single(run.Taskbars).Screen!.Info.Id);

        run.View.WriteProperty("page", "personalization");
        run.Settle();
        run.Control<ComboBox>("material").Emit("activated", 1);
        run.Settle();
        Assert.Equal(WindowBackdrop.Mica, run.Backend(Assert.Single(run.Taskbars)).Backdrop);

        run.Control<ComboBox>("colorScheme").Emit("activated", 2);
        run.Settle();
        Assert.True(Theme.Current.IsDark);

        // Nothing reloaded: the same generation runs.
        Assert.Same(generation, run.Host.Engine);
        Assert.DoesNotContain(run.Messages, m => m.StartsWith("scene:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_preferences_file_edited_by_hand_applies_as_it_is_saved()
    {
        using var run = new RunningShell("""{ "taskbarCentered": true }""");
        Assert.All(run.Taskbars, t => Assert.True((bool)t.ReadProperty("centered")!));

        // Past the time FileView takes its own writes to be.
        Thread.Sleep(300);
        File.WriteAllText(run.PreferencesPath, """{ "taskbarCentered": false, "taskbarAppsOn": "where" }""");
        run.Until(() => run.Taskbars.All(t => !(bool)t.ReadProperty("centered")!));
        Assert.All(run.Taskbars, t => Assert.Equal("where", t.ReadProperty("appsOn")));
    }

    [Fact]
    public void Hand_edits_apply_when_the_config_folder_did_not_exist_at_start()
    {
        // A new user has no %APPDATA%\winshell: the first choice makes it.
        using var run = new RunningShell(null, configFolder: false);
        run.Host.Ipc.Call("settings", "open", ["taskbarAutoHide"]);
        run.Settle();
        run.Click(run.Control<Switch>("taskbarAutoHide"));
        Assert.True(run.Saved().GetProperty("taskbarAutoHide").GetBoolean());

        Thread.Sleep(300);
        File.WriteAllText(run.PreferencesPath, """{ "taskbarAutoHide": false, "taskbarCentered": false }""");
        run.Until(() => run.Taskbars.All(t => !(bool)t.ReadProperty("centered")!));
        Assert.All(run.Taskbars, t => Assert.False((bool)t.ReadProperty("autoHide")!));
    }

    [Fact]
    public void Open_folder_makes_the_config_folder_it_opens()
    {
        using var run = new RunningShell(null, configFolder: false);
        run.Host.Ipc.Call("settings", "open", ["configFolder"]);
        run.Settle();
        var open = RunningShell.Walk(run.Settings.ContentItem).OfType<Button>().Single(b => b.ObjectName == "openConfigFolder");
        Assert.True(open.Enabled);
        run.Click(open);
        Assert.True(File.Exists(run.PreferencesPath));
        // Only the defaults: nothing was chosen.
        Assert.True(run.Saved().GetProperty("taskbarCentered").GetBoolean());
    }

    [Fact]
    public void One_taskbar_shows_the_apps_of_every_screen()
    {
        // Apps on the taskbar where their window is, then a taskbar on the main screen only.
        using var run = new RunningShell("""{ "taskbarAllScreens": false, "taskbarAppsOn": "where" }""");
        Assert.Equal("all", Assert.Single(run.Taskbars).ReadProperty("appsOn"));
        run.Host.Ipc.Call("settings", "open", ["taskbarAppsOn"]);
        run.Settle();
        Assert.False(run.Control<ComboBox>("taskbarAppsOn").Enabled);
    }

    [Theory]
    [InlineData("""{ "taskbarAppsOn": "sideways", "hiddenNotificationApps": {} }""")]
    [InlineData("""{ "taskbarAppsOn": 3, "hiddenNotificationApps": [null, 5, "Contoso.Chat", { "name": "No id" }, { "id": 7 }] }""")]
    public void Values_the_shell_cannot_use_fall_back_to_their_defaults(string preferences)
    {
        using var run = new RunningShell(preferences);
        Assert.All(run.Taskbars, t => Assert.Equal("all", t.ReadProperty("appsOn")));
        run.Host.Ipc.Call("settings", "open", ["taskbarAppsOn"]);
        run.Settle();
        Assert.Equal(0, run.Control<ComboBox>("taskbarAppsOn").ReadProperty("currentIndex"));
        run.Host.Ipc.Call("settings", "open", ["notifications"]);
        run.Settle();
        // The fake platform has no hotkeys; nothing else complains.
        Assert.DoesNotContain(run.Messages, m => !m.StartsWith("hotkey:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_right_click_on_the_taskbar_opens_Taskbar_settings()
    {
        using var run = new RunningShell(null);
        var taskbar = run.Taskbars[0];
        // The fake platform does not stretch a panel across its screen; Windows does.
        taskbar.OnResized(1920, 48, 1);
        run.Settle();
        var background = RunningShell.Walk(taskbar.ContentItem).Single(i => i.ObjectName == "taskbarBackground");
        var at = background.MapToScene(400, 24);
        taskbar.Scene.PointerPress(at.X, at.Y, QtEnums.RightButton);
        taskbar.Scene.PointerRelease(at.X, at.Y, QtEnums.RightButton);
        run.Settle();

        // Each taskbar has its menu; the one clicked opens.
        var menu = run.Popups.Single(p => p.Visible && RunningShell.Walk(p.ContentItem).Any(i => i.ObjectName == "menu:taskbarSettings"));

        Assert.True((bool)taskbar.ReadProperty("busy")!);
        var item = RunningShell.Walk(menu.ContentItem).Single(i => i.ObjectName == "menu:taskbarSettings");
        Assert.True(item.ActiveFocus);
        // Narrator reads the item inside a menu.
        var tree = new AccessibleTree(menu.Scene);
        tree.Refresh();
        static IEnumerable<AccessibleNode> Nodes(AccessibleNode n) => n.Children.SelectMany(Nodes).Prepend(n);
        var popup = Assert.Single(Nodes(tree.Root), n => n.Role == AccessibleAttached.RoleValue.PopupMenu);
        Assert.Equal("Taskbar settings", Assert.Single(popup.Children, n => n.Role == AccessibleAttached.RoleValue.MenuItem).Name);
        menu.Scene.KeyPress(new KeyEvent { Key = 0x01000004 });
        run.Settle();

        Assert.False(menu.Visible);
        Assert.True(run.Settings.Visible);
        Assert.Equal("taskbar", run.View.ReadProperty("page"));
        Assert.Equal("Winshell Settings", run.Settings.Title);
    }

    [Fact]
    public void Closing_Settings_hides_it_and_the_next_open_brings_it_back_where_it_was()
    {
        using var run = new RunningShell(null);
        run.Host.Ipc.Call("settings", "open", ["about"]);
        run.Settle();
        run.Settings.OnClosed();
        run.Settle();
        Assert.False(run.Settings.Visible);

        run.Host.Ipc.Call("settings", "open", [""]);
        run.Settle();
        Assert.True(run.Settings.Visible);
        Assert.Equal("about", run.View.ReadProperty("page"));
        Assert.True(((Item)RunningShell.Walk(run.Settings.ContentItem).Single(i => i.ObjectName == "search")).ActiveFocus);
    }

    /// <summary>The default shell under a <see cref="ShellHost"/>, with its folders in a temporary folder of its own.</summary>
    private sealed class RunningShell : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-settings-tests", Guid.NewGuid().ToString("N"));
        private readonly Theme _theme = Theme.Current;

        public RunningShell(string? preferences, bool configFolder = true)
        {
            var shell = Path.Combine(_directory, "shell");
            var config = Path.Combine(_directory, "config");
            Directory.CreateDirectory(shell);
            if (configFolder)
                Directory.CreateDirectory(config);
            Theme.Current = Theme.Default(false) with { AnimationsEnabled = false };
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "DefaultShell")))
            {
                var target = Path.Combine(shell, Path.GetFileName(file));
                if (Path.GetFileName(file) == "shell.qml")
                {
                    // A shell id and folders of its own, so nothing lands in the user's data or state.
                    var text = File.ReadAllText(file).Replace("//@ pragma ShellId winshell-default", $"""
                        //@ pragma ShellId winshell-settings-{Guid.NewGuid():N}
                        //@ pragma DataDir {Path.Combine(_directory, "data")}
                        //@ pragma StateDir {Path.Combine(_directory, "state")}
                        //@ pragma CacheDir {Path.Combine(_directory, "cache")}
                        """, StringComparison.Ordinal);
                    File.WriteAllText(target, text);
                }
                else
                {
                    File.Copy(file, target);
                }
            }
            PreferencesPath = Path.Combine(config, "preferences.json");
            if (preferences is not null)
                File.WriteAllText(PreferencesPath, preferences);

            var services = new FakeServices();
            var log = new Log { Sink = (_, category, message) => Messages.Add($"{category}: {message}"), MinimumLevel = LogLevel.Warn };
            Host = new ShellHost(Path.Combine(shell, "shell.qml"), Loop, Platform, log, () =>
            {
                var registry = StandardModules.Create();
                WindowsModules.Register(registry);
                ServiceModules.Register(registry);
                services.Register(registry);
                return registry;
            })
            { ConfigRoot = config };
            Host.WatchFiles = false;
            Assert.True(Host.Start(), string.Join(Environment.NewLine, Messages));
            Settle();
        }

        public RecordingPlatform Platform { get; } = new();

        public ManualEventLoop Loop { get; } = new();

        public ShellHost Host { get; }

        public List<string> Messages { get; } = [];

        public string PreferencesPath { get; }

        public PanelWindow[] Taskbars => Platform.Windows.Select(w => w.Client).OfType<PanelWindow>()
            .Where(p => p.HasProperty("centered")).OrderBy(p => p.Screen?.Info.Id).ToArray();

        public PopupWindow[] Popups => Platform.Windows.Select(w => w.Client).OfType<PopupWindow>().ToArray();

        public FloatingWindow Settings => Platform.Windows.Select(w => w.Client).OfType<FloatingWindow>().Single();

        public Item View => Walk(Settings.ContentItem).Single(i => i.HasProperty("notificationListener"));

        public RecordingWindow Backend(ShellWindow window) => Platform.Windows.Single(w => w.Client == window);

        public T Control<T>(string key) where T : Item =>
            Walk(Walk(Settings.ContentItem).Single(i => i.ObjectName == "setting:" + key && i.Visible)).OfType<T>().Single();

        public JsonElement Saved() => JsonDocument.Parse(File.ReadAllText(PreferencesPath)).RootElement;

        public void Settle()
        {
            Loop.Process();
            PolishQueue.Flush();
            Loop.Process();
        }

        /// <summary>Runs the loop until a file change has come through, or fails after five seconds.</summary>
        public void Until(Func<bool> done)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!done())
            {
                Assert.True(DateTime.UtcNow < until, "The change did not come through.");
                Thread.Sleep(20);
                Settle();
            }
        }

        public void Click(Item item)
        {
            var at = item.MapToScene(item.Width / 2, item.Height / 2);
            Settings.Scene.PointerPress(at.X, at.Y, QtEnums.LeftButton);
            Settings.Scene.PointerRelease(at.X, at.Y, QtEnums.LeftButton);
            Settle();
        }

        public static IEnumerable<Item> Walk(Item item)
        {
            yield return item;
            foreach (var child in item.ChildItems)
            {
                foreach (var found in Walk(child))
                    yield return found;
            }
        }

        public void Dispose()
        {
            Host.Dispose();
            Theme.Current = _theme;
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
