using Winshell.Platform.Windows;
using Winshell.Platform.Windows.Services.Input;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;
using Role = Winshell.Runtime.Quick.AccessibleAttached.RoleValue;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The Start surface: pinned grid, recommended rows, all apps, search, and the Windows key hook.
/// The view is given its apps, so these tests do not scan the real Start menu.
/// </summary>
public sealed class LauncherTests
{
    private const int Up = 0x01000013, Down = 0x01000015, Left = 0x01000012, Right = 0x01000014;
    private const int Return = 0x01000004, Escape = 0x01000000, Menu = 0x01000055, F10 = 0x01000039;

    [Fact]
    public void Layout_uses_windows_11_logical_pixels()
    {
        using var view = Open(dir => ViewQml(Apps(20), Recent(), PinIds(20)));
        var search = One(view.Root, "searchField");
        At(22, search.X);
        At(16, search.Y);
        At(596, search.Width);
        At(32, search.Height);
        Assert.Equal("Search apps", search.ReadProperty("placeholderText"));

        var scroll = One(view.Root, "pinScroll");
        At(236, scroll.Height);
        At(596, scroll.Width);
        Assert.True(scroll.Clip);
        At(316, Number(scroll.ReadProperty("contentHeight")));

        var tiles = Named(view.Root, "pinTile");
        Assert.Equal(20, tiles.Count);
        At(96, tiles[0].Width);
        At(76, tiles[0].Height);
        At(0, tiles[0].X);
        At(0, tiles[0].Y);
        At(100, tiles[1].X);
        At(0, tiles[1].Y);
        At(0, tiles[6].X);
        At(80, tiles[6].Y);
        At(100, tiles[7].X);
        At(80, tiles[7].Y);

        var recommended = Named(view.Root, "appRow");
        Assert.Equal(["Recent One", "Recent Two", "Recent Three"], recommended.Select(AppName));
        At(48, recommended[0].Height);
        At(32, One(recommended[0], "rowIcon").Width);

        var all = Named(view.Root, "allRow");
        Assert.Equal(23, all.Count);
        Assert.Equal("Pin 0", AppName(all[0]));
        Assert.Equal("Recent Three", AppName(all[^1]));
        At(40, all[0].Height);
        Assert.DoesNotContain(all, row => AppName(row) == "Hidden Tool");
        Assert.Empty(view.Messages);
    }

    [Fact]
    public void App_icons_stay_unloaded_until_start_is_open()
    {
        using var hidden = Open(_ => $$"""
            LauncherView {
              apps: [ {{App("note", "Notepad", icon: "C:/Apps/Notepad.lnk")}} ]
              recent: []
              pinIds: [ "note" ]
            }
            """);
        var image = Assert.Single(One(hidden.Root, "pinTile").ChildItems.OfType<Image>());
        Assert.Equal("", image.Source);
        Assert.Equal("", hidden.Eval("iconSource('C:/Apps/Notepad.lnk')"));
        Assert.Empty(hidden.Messages);

        using var open = Open(dir => ViewQml(Apps(1), Recent(), PinIds(1)));
        open.Eval("showIcons = true");
        open.Loop.Process();
        PolishQueue.Flush();
        var shortcut = "C:/Apps/Notepad.lnk";
        var packaged = @"shell:AppsFolder\Microsoft.Windows.Notepad";
        Assert.Equal("image://icon/" + Uri.EscapeDataString(shortcut), open.Eval("iconSource('C:/Apps/Notepad.lnk')"));
        Assert.Equal("image://icon/" + Uri.EscapeDataString(packaged), open.Eval("iconSource('shell:AppsFolder\\\\Microsoft.Windows.Notepad')"));
        Assert.Equal("", open.Eval("iconSource('')"));
        Assert.Empty(open.Messages);
    }

    [Fact]
    public void Arrows_move_through_the_grid_then_recommended_and_enter_launches()
    {
        using var view = Open(dir => ViewQml(Apps(8), Recent(), PinIds(8)));
        Assert.Equal(0, Number(view.Root.ReadProperty("selected")));
        Press(view, Right);
        Assert.Equal(1, Selected(view));
        Press(view, Left);
        Press(view, Left);
        Assert.Equal(0, Selected(view));
        Press(view, Right);
        for (var i = 0; i < 4; i++)
            Press(view, Right);
        Assert.Equal(5, Selected(view));
        Press(view, Down);
        Assert.Equal(6, Selected(view));
        Press(view, Down);
        Assert.Equal(8, Selected(view));
        Press(view, Up);
        Assert.Equal(6, Selected(view));
        Press(view, Down);
        Press(view, Return);
        Assert.Equal("Recent One", view.Eval("lastLaunched.name"));
        Press(view, Escape);
        Assert.Equal(1, Number(view.Root.ReadProperty("dismissCount")));
    }

    [Fact]
    public void Search_lists_the_given_hits_and_escape_clears_before_it_dismisses()
    {
        using var view = Open(dir => ViewQml(Apps(4), Recent(), PinIds(4)));
        view.Scene.TextInput("R");
        view.Loop.Process();
        PolishQueue.Flush();
        view.Eval(Filter);
        view.Loop.Process();
        PolishQueue.Flush();
        Assert.Equal("R", view.Eval("query"));
        var hits = Named(view.Root, "searchRow");
        Assert.Equal(["Recent One", "Recent Two", "Recent Three"], hits.Select(AppName));
        At(48, hits[0].Height);
        Assert.False(One(view.Root, "homeColumn").Visible);
        Press(view, Down);
        Assert.Equal(1, Selected(view));
        Press(view, Left);
        Assert.Equal(1, Selected(view));
        Press(view, Return);
        Assert.Equal("Recent Two", view.Eval("lastLaunched.name"));
        Assert.Equal("R", view.Eval("query"));

        view.Scene.TextInput("z");
        view.Loop.Process();
        view.Eval(Filter);
        view.Loop.Process();
        PolishQueue.Flush();
        Assert.Empty(Named(view.Root, "searchRow"));
        Assert.True(One(view.Root, "emptySearch").Visible);

        Press(view, Escape);
        Assert.Equal("", view.Eval("query"));
        Assert.Equal(0, Number(view.Root.ReadProperty("dismissCount")));
        Assert.True(One(view.Root, "homeColumn").Visible);
        Press(view, Escape);
        Assert.Equal(1, Number(view.Root.ReadProperty("dismissCount")));
    }

    [Fact]
    public void All_apps_is_one_column_and_escape_returns_home()
    {
        using var view = Open(dir => ViewQml(Apps(30), Recent(), PinIds(2)));
        view.Eval("showAll()");
        view.Loop.Process();
        PolishQueue.Flush();
        Assert.Equal("all", view.Eval("page"));
        Assert.False(One(view.Root, "homeColumn").Visible);
        Assert.True(One(view.Root, "allColumn").Visible);
        Press(view, Down);
        Assert.Equal(1, Selected(view));
        Press(view, Right);
        Assert.Equal(1, Selected(view));
        for (var i = 0; i < 20; i++)
            Press(view, Down);
        Assert.Equal(21, Selected(view));
        Assert.True(Number(One(view.Root, "allList").ReadProperty("contentY")) > 0);
        Press(view, Escape);
        Assert.Equal("home", view.Eval("page"));
        Assert.Equal(0, Number(view.Root.ReadProperty("dismissCount")));
    }

    [Fact]
    public void A_missing_pins_file_seeds_preferred_names_then_the_first_eight()
    {
        var pins = TempFile();
        var fallbackPath = TempFile();
        try
        {
            using var seeded = Open(dir => $$"""
                LauncherView {
                  pinsPath: "{{pins}}"
                  apps: [
                    {{App("clock", "Clock")}},
                    {{App("edge", "Microsoft Edge")}},
                    {{App("calc", "Calculator")}},
                    {{App("note", "Notepad")}},
                    {{App("other", "Something Else")}},
                    {{App("hid", "Hidden Tool", hidden: true)}}
                  ]
                }
                """);
            Assert.Equal(["edge", "note", "calc", "clock"], Strings(seeded.Eval("pinIds")));
            Assert.Equal(["Microsoft Edge", "Notepad", "Calculator", "Clock"], Named(seeded.Root, "pinTile").Select(AppName));
            var text = File.ReadAllText(pins);
            Assert.Contains("edge", text);
            Assert.DoesNotContain("hid", text);

            using var fallback = Open(dir => $$"""
                LauncherView {
                  pinsPath: "{{fallbackPath}}"
                  apps: [
                    {{string.Join(",\n", Enumerable.Range(0, 10).Select(i => App("a" + i, "App " + i)))}}
                  ]
                }
                """);
            Assert.Equal(Enumerable.Range(0, 8).Select(i => "a" + i), Strings(fallback.Eval("pinIds")));
        }
        finally
        {
            if (File.Exists(pins))
                File.Delete(pins);
            if (File.Exists(fallbackPath))
                File.Delete(fallbackPath);
        }
    }

    [Fact]
    public void An_empty_pins_file_stays_empty_and_unknown_ids_are_kept()
    {
        var empty = TempFile();
        var kept = TempFile();
        try
        {
            File.WriteAllText(empty, """{ "pins": [] }""");
            using var blank = Open(dir => $$"""
                LauncherView {
                  pinsPath: "{{empty}}"
                  apps: [ {{App("note", "Notepad")}}, {{App("edge", "Microsoft Edge")}} ]
                }
                """);
            Assert.Empty(Strings(blank.Eval("pinIds")));
            Assert.Empty(Named(blank.Root, "pinTile"));
            Assert.Equal("{\"pins\":[]}", Compact(File.ReadAllText(empty)));

            File.WriteAllText(kept, """{ "pins": ["ghost", "note"] }""");
            using var known = Open(dir => $$"""
                LauncherView {
                  pinsPath: "{{kept}}"
                  apps: [ {{App("note", "Notepad")}}, {{App("edge", "Edge")}} ]
                }
                """);
            Assert.Equal(["ghost", "note"], Strings(known.Eval("pinIds")));
            Assert.Equal(["Notepad"], Named(known.Root, "pinTile").Select(AppName));
            known.Eval("togglePin(pinnedApps[0])");
            known.Loop.Process();
            Assert.Equal(["ghost"], Strings(known.Eval("pinIds")));
            Assert.Contains("ghost", File.ReadAllText(kept));
            Assert.DoesNotContain("note", File.ReadAllText(kept));

            using var again = Open(dir => $$"""
                LauncherView {
                  pinsPath: "{{kept}}"
                  apps: [ {{App("note", "Notepad")}} ]
                }
                """);
            Assert.Equal(["ghost"], Strings(again.Eval("pinIds")));
            Assert.Empty(Named(again.Root, "pinTile"));
        }
        finally
        {
            File.Delete(empty);
            File.Delete(kept);
        }
    }

    [Fact]
    public void The_context_menu_pins_and_open_file_location_builds_a_command()
    {
        using var view = Open(dir => ViewQml(
            [App("note", "Notepad", icon: "C:/Apps/Notepad.lnk", target: "C:/Windows/notepad.exe")],
            [],
            ["\"note\""]));
        view.Eval("showMenuForSelection()");
        view.Loop.Process();
        PolishQueue.Flush();
        Assert.True((bool)view.Eval("appMenu.opened")!);
        Assert.Equal("Unpin from Start", view.Eval("pinItem.text"));
        Assert.Equal("Open file location", view.Eval("locationItem.text"));
        Assert.Equal(["explorer.exe", "/select,C:/Apps/Notepad.lnk"], Strings(view.Eval("locationCommand(currentApp())")));
        view.Eval("openLocation(currentApp())");
        Assert.Equal(["explorer.exe", "/select,C:/Apps/Notepad.lnk"], Strings(view.Eval("lastLocation")));
        Assert.Equal(
            ["explorer.exe", "shell:AppsFolder"],
            Strings(view.Eval("locationCommand({ id: 'store', name: 'Store', icon: 'shell:AppsFolder', target: 'shell:AppsFolder', packaged: true })")));
        Assert.Equal(
            ["explorer.exe", "shell:AppsFolder"],
            Strings(view.Eval("locationCommand({ id: 'x', name: 'X', icon: '', target: '', packaged: false })")));

        view.Eval("pinItem.triggered()");
        view.Loop.Process();
        PolishQueue.Flush();
        Assert.Empty(Strings(view.Eval("pinIds")));
        Assert.Equal("Pin to Start", view.Eval("pinItem.text"));
    }

    [Fact]
    public void Recommended_apps_can_be_turned_off()
    {
        using var view = Open(dir => ViewQml(Apps(2), Recent(), PinIds(2)).Replace("LauncherView {", "LauncherView { showRecommended: false;"));
        Assert.Empty(Strings(view.Eval("recommendedApps.map(a => a.id)")));
        Assert.Equal(["p0", "p1"], Strings(view.Eval("shown.map(a => a.id)")));
        Assert.False(One(view.Root, "recommendedHeader").Visible);
        Assert.Empty(view.Messages);
    }

    [Fact]
    public void Start_follows_its_pins_file_as_it_changes()
    {
        var pins = TempFile();
        try
        {
            File.WriteAllText(pins, """{ "pins": ["p0", "p1"] }""");
            using var view = Open(dir => $$"""
                LauncherView {
                  pinsPath: "{{pins}}"
                  apps: [ {{string.Join(", ", Apps(2))}} ]
                }
                """);
            Assert.Equal(["p0", "p1"], Strings(view.Eval("pinIds")));

            // Winshell Settings moves and unpins in the same file. Past the time a FileView takes its own writes to be:
            Thread.Sleep(300);
            File.WriteAllText(pins, """{ "pins": ["p1"] }""");
            var until = DateTime.UtcNow.AddSeconds(5);
            while (Strings(view.Eval("pinIds")).Count != 1)
            {
                Assert.True(DateTime.UtcNow < until, "Start did not follow the file.");
                Thread.Sleep(20);
                view.Loop.Process();
            }
            Assert.Equal(["Pin 1"], Named(view.Root, "pinTile").Select(AppName));
        }
        finally
        {
            File.Delete(pins);
        }
    }

    [Fact]
    public void Menu_key_and_shift_f10_open_the_context_menu()
    {
        using var view = Open(dir => ViewQml(Apps(2), [], PinIds(2)));
        Press(view, Menu);
        view.Loop.Process();
        Assert.True((bool)view.Eval("appMenu.opened")!);
        view.Eval("appMenu.close()");
        view.Loop.Process();
        Press(view, F10, QtEnums.ShiftModifier);
        view.Loop.Process();
        Assert.True((bool)view.Eval("appMenu.opened")!);
    }

    [Fact]
    public void Selection_uses_the_subtle_fill_after_the_motion()
    {
        using var view = Open(dir => ViewQml(Apps(2), [], PinIds(2)));
        view.Loop.Advance(TimeSpan.FromMilliseconds(120));
        PolishQueue.Flush();
        var fills = Named(view.Root, "pinFill");
        var selected = Theme.Current.SubtleFillColorSecondary;
        Assert.Equal(selected, Color(fills[0]));
        Press(view, Right);
        view.Loop.Advance(TimeSpan.FromMilliseconds(120));
        PolishQueue.Flush();
        Assert.Equal(QColor.Transparent, Color(fills[0]));
        Assert.Equal(selected, Color(fills[1]));
    }

    [Fact]
    public void Search_apps_and_sections_are_exposed_to_assistive_technology()
    {
        using var view = Open(dir => ViewQml(Apps(2), Recent(), PinIds(2)));
        var tree = new AccessibleTree(view.Scene);
        tree.Refresh();
        var nodes = Flatten(tree.Root).Select(n => (n.Role, n.Name)).ToList();
        Assert.Contains((Role.EditableText, "Search apps"), nodes);
        Assert.Contains((Role.Heading, "Pinned"), nodes);
        Assert.Contains((Role.Heading, "Recommended"), nodes);
        Assert.Contains((Role.Button, "All apps"), nodes);
        Assert.Contains((Role.Button, "Pin 0"), nodes);
        Assert.Contains((Role.Button, "Recent One"), nodes);

        view.Eval("showAll()");
        view.Loop.Process();
        PolishQueue.Flush();
        tree.Refresh();
        nodes = Flatten(tree.Root).Select(n => (n.Role, n.Name)).ToList();
        Assert.Contains((Role.Button, "Back"), nodes);
        Assert.Contains((Role.Heading, "All apps"), nodes);
        Assert.Contains((Role.Button, "Pin 0"), nodes);
    }

    [Fact]
    public void The_windows_key_is_wired_without_installing_the_hook()
    {
        var dir = TempDir();
        try
        {
            foreach (var file in Directory.GetFiles(DefaultShellDir()))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            var registry = StandardModules.Create();
            WindowsModules.Register(registry);
            ServiceModules.Register(registry);
            var loop = new ManualEventLoop();
            using var engine = new QmlEngine(registry, loop, new Log(), dir);
            var root = (ShellRoot)engine.CreateRoot(Path.Combine(dir, "shell.qml"));
            loop.Process();
            // Start's hotkey is the Windows key until Preferences says otherwise; Winshell Settings has none and is idle.
            var hotkeys = root.Children.OfType<Hotkey>().ToArray();
            var hotkey = Assert.Single(hotkeys, h => h.Sequence != "");
            Assert.Equal("", Assert.Single(hotkeys, h => h.Sequence == "").Error);
            Assert.Equal("Win", hotkey.Sequence);
            Assert.Equal("Hotkeys are not available.", hotkey.Error);
            Assert.False(hotkey.Active);
            Assert.False(WindowsHotkeyRegistrar.IsHookInstalled);
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

    private const string Filter = """
        searchResults = (function () {
          var q = String(query || "").toLowerCase()
          var out = []
          var rows = apps
          for (var i = 0; i < rows.length; i++) {
            var name = String(rows[i].name || "").toLowerCase()
            if (!rows[i].hidden && q.length > 0 && name.indexOf(q) === 0)
              out.push(rows[i])
          }
          return out
        })()
        """;

    private static string ViewQml(IEnumerable<string> apps, IEnumerable<string> recent, IEnumerable<string> pinIds) => $$"""
        LauncherView {
          apps: [ {{string.Join(", ", apps)}} ]
          recent: [ {{string.Join(", ", recent)}} ]
          pinIds: [ {{string.Join(", ", pinIds)}} ]
        }
        """;

    private static IEnumerable<string> Apps(int count)
    {
        for (var i = 0; i < count; i++)
            yield return App("p" + i, "Pin " + i);
        yield return App("r0", "Recent One");
        yield return App("r1", "Recent Two");
        yield return App("r2", "Recent Three");
        yield return App("hid", "Hidden Tool", hidden: true);
    }

    private static IEnumerable<string> Recent() =>
    [
        App("r0", "Recent One"),
        App("r1", "Recent Two"),
        App("hid", "Hidden Tool", hidden: true),
        App("p0", "Pin 0"),
        App("r2", "Recent Three"),
    ];

    private static IEnumerable<string> PinIds(int count)
    {
        for (var i = 0; i < count; i++)
            yield return "\"p" + i + "\"";
    }

    private static string App(string id, string name, bool hidden = false, string icon = "", string target = "", bool packaged = false) =>
        $"{{ id: \"{id}\", name: \"{name}\", hidden: {(hidden ? "true" : "false")}, icon: \"{icon}\", target: \"{target}\", packaged: {(packaged ? "true" : "false")} }}";

    private static int Selected(Shell view) => (int)Number(view.Root.ReadProperty("selected"));

    private static void Press(Shell view, int key, int modifiers = 0)
    {
        view.Scene.KeyPress(new KeyEvent { Key = key, Modifiers = modifiers });
        view.Scene.KeyRelease(new KeyEvent { Key = key, Modifiers = modifiers });
        view.Loop.Process();
        PolishQueue.Flush();
    }

    private static string AppName(Item item) => (string)item.ReadProperty("appName")!;

    private static QColor Color(Item item) => (QColor)item.ReadProperty("color")!;

    private static void At(double expected, double actual) => Assert.Equal(expected, actual, 3);

    private static double Number(object? value) => value switch
    {
        double d => d,
        int i => i,
        float f => f,
        _ => throw new Xunit.Sdk.XunitException($"expected a number, got {value}"),
    };

    private static List<string> Strings(object? value)
    {
        var list = Assert.IsAssignableFrom<IEnumerable<object?>>(value);
        return list.Select(v => Assert.IsType<string>(v)).ToList();
    }

    private static string Compact(string json) => json.Replace(" ", "").Replace("\r", "").Replace("\n", "");

    private static Item One(Item root, string name) => Assert.Single(Named(root, name));

    private static List<Item> Named(Item root, string name)
    {
        var found = new List<Item>();
        Walk(root);
        return found;

        void Walk(Item item)
        {
            if (item.ObjectName == name)
                found.Add(item);
            foreach (var child in item.ChildItems)
                Walk(child);
        }
    }

    private static IEnumerable<AccessibleNode> Flatten(AccessibleNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var nested in Flatten(child))
                yield return nested;
        }
    }

    private static Shell Open(Func<string, string> qml)
    {
        var dir = TempDir();
        foreach (var name in new[] { "launcher.js", "LauncherView.qml" })
            File.Copy(Path.Combine(DefaultShellDir(), name), Path.Combine(dir, name));
        var body = "import QtQuick\nimport Winshell.Theme\n" + qml(dir);
        File.WriteAllText(Path.Combine(dir, "shell.qml"), body);
        return new Shell(dir);
    }

    private static string DefaultShellDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Winshell", "DefaultShell");
            if (File.Exists(Path.Combine(candidate, "LauncherView.qml")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("DefaultShell");
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-launcher-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path.Replace('\\', '/');
    }

    private sealed class Shell : IDisposable
    {
        private readonly string _directory;
        private readonly QmlEngine _engine;

        public Shell(string directory)
        {
            _directory = directory;
            Loop = new ManualEventLoop();
            var registry = StandardModules.Create();
            WindowsModules.Register(registry);
            _engine = new QmlEngine(registry, Loop, new Log { Sink = (level, category, message) => Messages.Add($"{level} {category}: {message}") }, directory);
            Root = (Item)_engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            Scene = new Scene();
            Scene.Root.SetLayoutWidth(640);
            Scene.Root.SetLayoutHeight(720);
            Root.SetLayoutWidth(640);
            Root.SetLayoutHeight(720);
            Root.SetParentItem(Scene.Root);
            Eval("focusSearch()");
            Loop.Process();
            PolishQueue.Flush();
        }

        public ManualEventLoop Loop { get; }

        public Item Root { get; }

        public Scene Scene { get; }

        public List<string> Messages { get; } = [];

        public object? Eval(string expression)
        {
            var scope = new ScopeObject(_engine.Js, Root, Root.CreationContext!);
            var fn = _engine.Js.Compile("return (" + expression + ");", [], scope, "test");
            return ValueConverter.FromJs(_engine.Js.Engine.Call(fn, _engine.Js.Wrap(Root), []));
        }

        public void Dispose()
        {
            _engine.Dispose();
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
