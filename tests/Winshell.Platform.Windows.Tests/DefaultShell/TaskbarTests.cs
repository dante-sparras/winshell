using SkiaSharp;
using Winshell.Platform.Windows.Accessibility;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>The default shell's taskbar, against a fake window list and app index.</summary>
[Collection(ThemeCollection.Name)]
public class TaskbarTests
{
    private const string Tasks = """
        import QtQuick
        import Winshell.Windows
        import Winshell.Apps
        TaskList {
          windows: WindowList.windows.values
          activeWindow: WindowList.activeWindow
          apps: Apps
          pinned: ["Microsoft.Windows.Explorer", "Microsoft.WindowsStore_8wekyb3d8bbwe!App", "C:\\Tools\\portable.exe"]
          property var requests: []
          onPinRequested: id => requests = requests.concat(["pin " + id])
          onUnpinRequested: id => requests = requests.concat(["unpin " + id])
        }
        """;

    private const int Shift = 0x02000000;

    /// <summary>File Explorer and the Store pinned, File Explorer open, two terminals, one in front, and a program the index does not have.</summary>
    private sealed class Desk
    {
        public Desk()
        {
            Explorer = Services.Apps.Add("File Explorer", "File Explorer", "Microsoft.Windows.Explorer");
            Store = Services.Apps.Add("Microsoft.WindowsStore_8wekyb3d8bbwe!App", "Microsoft Store", "Microsoft.WindowsStore_8wekyb3d8bbwe!App");
            Terminal = Services.Apps.Add("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
            Documents = Add(new FakeAppWindow("Documents", "Microsoft.Windows.Explorer", @"C:\Windows\explorer.exe"));
            Shell1 = Add(new FakeAppWindow("PowerShell", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", @"C:\Program Files\WindowsApps\Terminal\WindowsTerminal.exe"));
            Shell2 = Add(new FakeAppWindow("Command Prompt", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", @"C:\Program Files\WindowsApps\Terminal\WindowsTerminal.exe"));
            Tool = Add(new FakeAppWindow("Tool - untitled", "tool", @"C:\Tools\tool.exe"));
            Services.WindowList.Activate(Shell1);
        }

        public FakeServices Services { get; } = new();
        public FakeAppEntry Explorer { get; }
        public FakeAppEntry Store { get; }
        public FakeAppEntry Terminal { get; }
        public FakeAppWindow Documents { get; }
        public FakeAppWindow Shell1 { get; }
        public FakeAppWindow Shell2 { get; }
        public FakeAppWindow Tool { get; }

        private FakeAppWindow Add(FakeAppWindow window)
        {
            Services.WindowList.Windows.Add(window);
            return window;
        }
    }

    private static Item[] Buttons(DefaultShellHarness shell) => DefaultShellHarness.FindAll(shell.Root)
        .Where(i => i.ObjectName.StartsWith("task:", StringComparison.Ordinal) && i.Visible)
        .OrderBy(i => i.MapToScene(0, 0).X)
        .ToArray();

    private static Item Button(DefaultShellHarness shell, string key) => shell.Find("task:" + key);

    private static void Press(DefaultShellHarness shell, Item item, int button, int modifiers = 0)
    {
        var p = item.MapToScene(item.Width / 2, item.Height / 2);
        shell.Scene.PointerPress(p.X, p.Y, button, modifiers);
        shell.Scene.PointerRelease(p.X, p.Y, button, modifiers);
        shell.Settle();
    }

    [Fact]
    public void Pinned_apps_come_first_then_running_apps_each_on_one_button()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        Assert.Equal(
            ["task:app:file explorer", "task:app:microsoft.windowsstore_8wekyb3d8bbwe!app", "task:window:portable", "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "task:window:tool"],
            Buttons(shell).Select(b => b.ObjectName));
    }

    [Fact]
    public void Buttons_are_40_pixels_square_4_apart()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var buttons = Buttons(shell);
        Assert.All(buttons, b => Assert.Equal((40.0, 40.0), (b.Width, b.Height)));
        Assert.Equal([0.0, 44, 88, 132, 176], buttons.Select(b => b.MapToScene(0, 0).X));
        Assert.Equal(buttons.Length * 44 - 4, shell.Root.ImplicitWidth);
    }

    [Fact]
    public void A_pin_waits_for_the_app_index_and_appears_when_the_app_is_there()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks.Replace("\"C:\\\\Tools\\\\portable.exe\"", "\"Contoso.Notes\""), width: 600, height: 48, services: desk.Services);
        Assert.False(shell.Has("task:app:contoso.notes"));
        desk.Services.Apps.Add("Contoso.Notes", "Notes", "Contoso.Notes");
        shell.Settle();
        Assert.Equal("task:app:contoso.notes", Buttons(shell)[2].ObjectName);
    }

    [Fact]
    public void Narrator_reads_each_app_with_its_windows_the_way_Explorer_names_them()
    {
        var desk = new Desk();
        desk.Tool.Attention = true;
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();

        Assert.Equal(AccessibleAttached.RoleValue.ToolBar, tree.Find(shell.Root)!.Role);
        Assert.Equal("Running applications", tree.Find(shell.Root)!.Name);
        var names = Buttons(shell).Select(b => tree.Find(b)!).ToArray();
        Assert.All(names, n => Assert.Equal(AccessibleAttached.RoleValue.Button, n.Role));
        Assert.Equal(
            ["File Explorer - 1 running window", "Microsoft Store pinned", "portable pinned", "Terminal - 2 running windows", "Tool - untitled - 1 running window"],
            names.Select(n => n.Name));
        Assert.Equal("Needs your attention", names[4].Description);
        Assert.Equal("", names[0].Description);
    }

    [Fact]
    public void A_click_brings_a_window_forward_minimizes_it_when_in_front_and_launches_a_pinned_app()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);

        shell.Click(Button(shell, "app:file explorer"));
        Assert.Equal(["activate"], desk.Documents.Calls);

        desk.Services.WindowList.Activate(desk.Documents);
        shell.Settle();
        shell.Click(Button(shell, "app:file explorer"));
        Assert.Equal(["activate", "minimize"], desk.Documents.Calls);

        shell.Click(Button(shell, "app:microsoft.windowsstore_8wekyb3d8bbwe!app"));
        Assert.Equal(1, desk.Store.Launches);
    }

    [Fact]
    public void A_click_on_an_app_with_several_windows_goes_to_the_next_one_or_back_to_the_one_used_last()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var terminal = Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");

        // PowerShell is in front: the click goes to the next window.
        shell.Click(terminal);
        Assert.Equal(["activate"], desk.Shell2.Calls);

        // From another app, the click goes back to the terminal window used last.
        desk.Services.WindowList.Activate(desk.Shell2);
        shell.Settle();
        desk.Services.WindowList.Activate(desk.Documents);
        shell.Settle();
        shell.Click(terminal);
        Assert.Equal(["activate", "activate"], desk.Shell2.Calls);
        Assert.Empty(desk.Shell1.Calls);
    }

    [Fact]
    public void A_middle_or_shift_click_opens_a_new_window_of_the_app()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var terminal = Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        Press(shell, terminal, QtEnums.MiddleButton);
        Press(shell, terminal, QtEnums.LeftButton, Shift);
        Assert.Equal(2, desk.Terminal.Launches);
        Assert.Empty(desk.Shell1.Calls);
        Assert.Empty(desk.Shell2.Calls);
    }

    [Fact]
    public void Narrator_invokes_a_button_as_a_click()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        tree.Find(Button(shell, "app:microsoft.windowsstore_8wekyb3d8bbwe!app"))!.Invoke();
        shell.Settle();
        Assert.Equal(1, desk.Store.Launches);
    }

    [Fact]
    public void The_menu_has_the_app_pin_or_unpin_and_close()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var menu = (QObject)shell.Root.ReadProperty("menu")!;

        Press(shell, Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app"), QtEnums.RightButton);
        Assert.True((bool)menu.ReadProperty("visible")!);
        Assert.Equal(["Terminal", "Pin to taskbar", "Close all windows"], MenuLabels(menu));

        Press(shell, Button(shell, "app:file explorer"), QtEnums.RightButton);
        Assert.Equal(["File Explorer", "Unpin from taskbar", "Close window"], MenuLabels(menu));

        Press(shell, Button(shell, "app:microsoft.windowsstore_8wekyb3d8bbwe!app"), QtEnums.RightButton);
        Assert.Equal(["Microsoft Store", "Unpin from taskbar"], MenuLabels(menu));
    }

    [Fact]
    public void Menu_items_pin_unpin_launch_and_close_every_window()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var menu = (ShellWindow)shell.Root.ReadProperty("menu")!;

        void Choose(string key, string item)
        {
            Press(shell, Button(shell, key), QtEnums.RightButton);
            var row = DefaultShellHarness.FindAll(menu.Scene.Root).First(i => i.ObjectName == "menu:" + item);
            var p = row.MapToScene(row.Width / 2, row.Height / 2);
            menu.Scene.PointerPress(p.X, p.Y, QtEnums.LeftButton);
            menu.Scene.PointerRelease(p.X, p.Y, QtEnums.LeftButton);
            shell.Settle();
            Assert.False(menu.Visible);
        }

        Choose("app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "pin");
        Choose("app:file explorer", "unpin");
        Choose("window:tool", "pin");
        Choose("app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "close");
        Choose("app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "launch");

        Assert.Equal(
            ["pin Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "unpin Microsoft.Windows.Explorer", @"pin C:\Tools\tool.exe"],
            ((IEnumerable<object?>)shell.Root.ReadProperty("requests")!).Select(r => r?.ToString()));
        Assert.Equal(["close"], desk.Shell1.Calls);
        Assert.Equal(["close"], desk.Shell2.Calls);
        Assert.Equal(1, desk.Terminal.Launches);
    }

    [Fact]
    public void The_line_under_an_app_is_long_and_accented_in_front_short_and_grey_behind_and_absent_when_not_running()
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(true);
            var desk = new Desk();
            using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
            Item Indicator(string key) => DefaultShellHarness.FindAll(Button(shell, key)).First(i => i.ObjectName == "taskIndicator");

            var front = Indicator("app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
            var behind = Indicator("app:file explorer");
            Assert.False(Indicator("app:microsoft.windowsstore_8wekyb3d8bbwe!app").Visible);
            Assert.Equal((16.0, 3.0), (front.Width, front.Height));
            Assert.Equal((6.0, 3.0), (behind.Width, behind.Height));
            Assert.Equal(Theme.Current.AccentFillColorDefault, front.ReadProperty("color"));
            Assert.Equal(Theme.Current.TextFillColorTertiary, behind.ReadProperty("color"));

            using var bitmap = shell.Paint(SKColors.Black);
            var center = front.MapToScene(front.Width / 2, front.Height / 2);
            AssertNear(Theme.Current.AccentFillColorDefault, bitmap.GetPixel((int)center.X, (int)center.Y));
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    [Fact]
    public void A_window_that_wants_attention_marks_its_button_until_it_is_activated()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var button = Button(shell, "window:tool");
        var indicator = DefaultShellHarness.FindAll(button).First(i => i.ObjectName == "taskIndicator");

        desk.Tool.Attention = true;
        shell.Settle();
        Assert.True((bool)button.ReadProperty("attention")!);
        Assert.Equal(Theme.Current.SystemFillColorCritical, indicator.ReadProperty("color"));

        desk.Tool.Attention = false;
        desk.Services.WindowList.Activate(desk.Tool);
        shell.Settle();
        Assert.False((bool)button.ReadProperty("attention")!);
        Assert.Equal(Theme.Current.AccentFillColorDefault, indicator.ReadProperty("color"));
    }

    [Fact]
    public void A_new_window_title_is_read_without_rebuilding_the_buttons()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var before = Buttons(shell);
        desk.Tool.Title = "Tool - saved";
        shell.Settle();
        var after = Buttons(shell);
        Assert.Equal(before.Length, after.Length);
        Assert.All(before.Zip(after), pair => Assert.Same(pair.First, pair.Second));
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        Assert.Equal("Tool - saved - 1 running window", tree.Find(Button(shell, "window:tool"))!.Name);
    }

    private static PopupWindow Popup(DefaultShellHarness shell, string name) => (PopupWindow)shell.Root.ReadProperty(name)!;

    private static void Hover(DefaultShellHarness shell, Item item)
    {
        var p = item.MapToScene(item.Width / 2, item.Height / 2);
        shell.Scene.PointerMove(p.X, p.Y);
        shell.Settle();
    }

    [Fact]
    public void A_window_that_opens_or_closes_keeps_every_other_apps_button()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var before = Buttons(shell).ToDictionary(b => b.ObjectName);

        var notes = new FakeAppWindow("Notes", "notes", @"C:\Tools\notes.exe");
        desk.Services.WindowList.Windows.Add(notes);
        desk.Services.WindowList.Windows.Add(new FakeAppWindow("Windows PowerShell", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        shell.Settle();
        desk.Services.WindowList.Windows.Remove(desk.Shell2);
        shell.Settle();

        var after = Buttons(shell).ToDictionary(b => b.ObjectName);
        Assert.Contains("task:window:notes", after.Keys);
        foreach (var (name, button) in before)
        {
            Assert.Same(button, after[name]);
            Assert.False(button.IsDestroyed);
        }
    }

    [Fact]
    public void A_regroup_before_the_preview_opens_still_opens_it_at_the_live_button()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var terminal = Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        var preview = Popup(shell, "preview");

        Hover(shell, terminal);
        desk.Services.WindowList.Windows.Add(new FakeAppWindow("Notes", "notes", @"C:\Tools\notes.exe"));
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));

        Assert.True(preview.Visible);
        Assert.Same(terminal, preview.Anchor.Item);
        Assert.False(terminal.IsDestroyed);
    }

    [Fact]
    public void An_open_preview_and_menu_stay_on_their_button_while_other_apps_come_and_go()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var terminal = Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        var preview = Popup(shell, "preview");
        Hover(shell, terminal);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(preview.Visible);

        desk.Services.WindowList.Windows.Add(new FakeAppWindow("Notes", "notes", @"C:\Tools\notes.exe"));
        shell.Settle();
        desk.Services.WindowList.Windows.Remove(desk.Tool);
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.Same(terminal, preview.Anchor.Item);
        Assert.False(terminal.IsDestroyed);

        var menu = Popup(shell, "menu");
        Press(shell, terminal, QtEnums.RightButton);
        Assert.True(menu.Visible);
        desk.Services.WindowList.Windows.Add(new FakeAppWindow("Paint", "paint", @"C:\Tools\paint.exe"));
        shell.Settle();
        Assert.True(menu.Visible);
        Assert.Same(terminal, menu.Anchor.Item);
        Assert.False(terminal.IsDestroyed);
    }

    [Fact]
    public void Closing_windows_from_the_preview_keeps_it_until_the_app_is_gone()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var tool = Button(shell, "window:tool");
        var terminal = Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        var preview = Popup(shell, "preview");

        // One of the terminal's two windows closes: the button and the preview stay, with one window.
        Hover(shell, terminal);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        desk.Services.WindowList.Windows.Remove(desk.Shell1);
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.Same(terminal, preview.Anchor.Item);
        Assert.Equal(["preview:Command Prompt"], DefaultShellHarness.FindAll(preview.Scene.Root).Where(i => i.ObjectName.StartsWith("preview:", StringComparison.Ordinal)).Select(i => i.ObjectName));

        // The tool's only window closes: its button goes, and the preview open on it closes with it.
        Hover(shell, tool);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Same(tool, preview.Anchor.Item);
        desk.Services.WindowList.Windows.Remove(desk.Tool);
        shell.Settle();
        Assert.False(preview.Visible);
        Assert.False(shell.Has("task:window:tool"));
    }

    [Fact]
    public void The_menu_closes_when_its_app_goes()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var menu = Popup(shell, "menu");
        Press(shell, Button(shell, "window:tool"), QtEnums.RightButton);
        Assert.True(menu.Visible);
        desk.Services.WindowList.Windows.Remove(desk.Tool);
        shell.Settle();
        Assert.False(menu.Visible);
    }

    [Fact]
    public void A_closed_window_leaves_and_an_unpinned_app_with_no_windows_goes()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        desk.Services.WindowList.Windows.Remove(desk.Tool);
        desk.Services.WindowList.Windows.Remove(desk.Documents);
        shell.Settle();
        Assert.False(shell.Has("task:window:tool"));
        Assert.Equal("File Explorer pinned", new AccessibleTree(shell.Scene).Also(t => t.Refresh()).Find(Button(shell, "app:file explorer"))!.Name);
    }

    private static ShellScreen Screen(string id, double x) =>
        new(new ScreenInfo(id, id, "", "", "", new QRect(x, 0, 1920, 1080), new QRect(x, 0, 1920, 1032), 1, 0, 0, 60, x == 0));

    [Theory]
    [InlineData(true, true, new[] { "app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app", "window:portable", "app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "window:tool" })]
    [InlineData(false, true, new[] { "app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app", "window:portable", "window:tool" })]
    [InlineData(false, false, new[] { "window:tool" })]
    public void With_several_screens_a_taskbar_shows_the_windows_and_pins_its_setting_asks_for(bool allScreens, bool showPins, string[] keys)
    {
        var desk = new Desk();
        var left = Screen("DISPLAY1", 0);
        var right = Screen("DISPLAY2", 1920);
        desk.Documents.Screens = [right];
        desk.Shell1.Screens = [right];
        desk.Shell2.Screens = [right];
        desk.Tool.Screens = [left];
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        shell.Root.WriteProperty("screen", left);
        shell.Root.WriteProperty("allScreens", allScreens);
        shell.Root.WriteProperty("showPins", showPins);
        shell.Settle();
        Assert.Equal(keys.Select(k => "task:" + k), Buttons(shell).Select(b => b.ObjectName));
    }

    private const string Bar = """
        import QtQuick
        import Winshell
        Item {
          property var started: []
          property int searched: 0
          property QtObject pins: QtObject {
            property var ids: ["Microsoft.Windows.Explorer"]
            function pin(id) { ids = ids.concat([id]) }
            function unpin(id) { ids = ids.filter(i => i !== id) }
          }
          property Taskbar bar: Taskbar {
            onStartRequested: button => started = started.concat([button.objectName])
            onSearchRequested: searched++
          }
          Component.onCompleted: bar.pins = pins
        }
        """;

    private static (DefaultShellHarness Shell, PanelWindow Bar) Taskbar(Desk desk, double width = 1920)
    {
        var shell = new DefaultShellHarness(Bar, width: 100, height: 100, services: desk.Services);
        var bar = (PanelWindow)shell.Root.ReadProperty("bar")!;
        bar.OnResized(width, 48, 1);
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        return (shell, bar);
    }

    private static Item In(PanelWindow bar, string objectName) =>
        DefaultShellHarness.FindAll(bar.Scene.Root).First(i => i.ObjectName == objectName && i.Visible);

    private static void Click(DefaultShellHarness shell, PanelWindow bar, Item item)
    {
        var p = item.MapToScene(item.Width / 2, item.Height / 2);
        bar.Scene.PointerPress(p.X, p.Y, QtEnums.LeftButton);
        bar.Scene.PointerRelease(p.X, p.Y, QtEnums.LeftButton);
        shell.Settle();
    }

    [Fact]
    public void The_taskbar_is_48_pixels_along_the_bottom_and_reserves_that_edge()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        Assert.Equal(Edges.Bottom | Edges.Left | Edges.Right, bar.Anchors.Edges);
        Assert.Equal(48, bar.ImplicitHeight);
        Assert.Equal(48, bar.ComputedExclusiveZone);
        Assert.Equal(0, bar.Margins.Bottom);
    }

    [Fact]
    public void Start_search_and_the_apps_sit_in_the_middle_or_on_the_left()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        var apps = In(bar, "taskbarApps");
        var start = In(bar, "startButton");
        Assert.Equal(Math.Round((1920 - apps.Width) / 2), apps.X);
        Assert.Equal(apps.X, start.MapToScene(0, 0).X);
        Assert.Equal(apps.X + 44, In(bar, "searchButton").MapToScene(0, 0).X);
        Assert.Equal(4, start.MapToScene(0, 0).Y);

        bar.WriteProperty("centered", false);
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(12, apps.X);
    }

    [Fact]
    public void Start_and_Search_hand_their_button_to_the_shell_and_show_when_what_they_open_is_open()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        Click(shell, bar, In(bar, "startButton"));
        Click(shell, bar, In(bar, "searchButton"));
        Assert.Equal(["startButton"], ((IEnumerable<object?>)shell.Root.ReadProperty("started")!).Select(o => o?.ToString()));
        Assert.Equal(1, Convert.ToInt32(shell.Root.ReadProperty("searched")));

        bar.WriteProperty("startOpen", true);
        shell.Settle();
        Assert.True((bool)In(bar, "startButton").ReadProperty("checked")!);
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        Assert.Equal("Taskbar", tree.Root.Name);
        Assert.Equal("Start", tree.Find(In(bar, "startButton"))!.Name);
        Assert.Equal("Search", tree.Find(In(bar, "searchButton"))!.Name);
    }

    [Fact]
    public void Pins_and_unpins_from_the_menu_reach_the_pinned_list()
    {
        var desk = new Desk();
        var (shell, bar) = Taskbar(desk);
        using var _ = shell;
        Assert.Contains(DefaultShellHarness.FindAll(bar.Scene.Root), i => i.ObjectName == "task:app:file explorer");
        var list = (Item)bar.ReadProperty("taskList")!;
        list.Emit("pinRequested", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
        list.Emit("unpinRequested", "Microsoft.Windows.Explorer");
        shell.Settle();
        var pins = (QObject)shell.Root.ReadProperty("pins")!;
        Assert.Equal(["Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"], Ids(pins));
    }

    [Fact]
    public void An_auto_hiding_taskbar_reserves_nothing_and_slides_down_to_a_2_pixel_line()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        bar.WriteProperty("autoHide", true);
        shell.Settle();
        Assert.Equal(0, bar.ComputedExclusiveZone);
        Assert.False((bool)bar.ReadProperty("revealed")!);

        // The pointer on the line brings it back.
        bar.Scene.PointerMove(500, 47);
        shell.Settle();
        Assert.True((bool)bar.ReadProperty("revealed")!);

        // It stays while the pointer is on it, and leaves a second after the pointer does.
        shell.Loop.Advance(TimeSpan.FromSeconds(3));
        Assert.True((bool)bar.ReadProperty("revealed")!);
        bar.Scene.PointerLeave();
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True((bool)bar.ReadProperty("revealed")!);
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        Assert.False((bool)bar.ReadProperty("revealed")!);
    }

    /// <summary>Where the platform puts the panel on a monitor, as Win32Window does for its exclusion mode.</summary>
    private static Winshell.Platform.Windows.Native.RECT Placed(PanelWindow bar, Winshell.Platform.Windows.Native.RECT monitor, double scale)
    {
        Assert.Equal(1, bar.ExclusionMode);
        var placement = new PanelPlacement(null, bar.Anchors.Edges, bar.Margins.Left, bar.Margins.Top, bar.Margins.Right, bar.Margins.Bottom,
            bar.ImplicitWidth, bar.ImplicitHeight, bar.ExclusionMode, bar.ComputedExclusiveZone, true, false, "winshell");
        // Ignore: the whole monitor.
        return DesktopSpace.Anchor(monitor, placement, scale);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void An_auto_hiding_taskbar_never_reaches_the_screen_below_while_it_slides(double scale)
    {
        // This screen, with another directly below it.
        var upper = new Winshell.Platform.Windows.Native.RECT(0, 0, (int)(1920 * scale), (int)(1080 * scale));
        var lower = new Winshell.Platform.Windows.Native.RECT(0, upper.Bottom, upper.Right, upper.Bottom + (int)(1080 * scale));
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        var start = In(bar, "startButton");
        var heights = new HashSet<int>();

        void Frames()
        {
            for (var i = 0; i < 30; i++)
            {
                shell.Loop.Advance(TimeSpan.FromMilliseconds(16));
                var rect = Placed(bar, upper, scale);
                heights.Add(rect.Height);
                Assert.True(rect.Top >= upper.Top && rect.Bottom == upper.Bottom, $"{rect.Top}..{rect.Bottom} leaves {upper.Top}..{upper.Bottom}");
                Assert.True(rect.Bottom <= lower.Top);
                // The taskbar's items stay where they are in it; the window shows its top part.
                Assert.Equal(4, start.MapToScene(0, 0).Y);
            }
        }

        bar.WriteProperty("autoHide", true);
        Frames();
        Assert.Equal((int)Math.Round(2 * scale), Placed(bar, upper, scale).Height);

        bar.Scene.PointerMove(500, 1);
        Frames();
        Assert.Equal((int)Math.Round(48 * scale), Placed(bar, upper, scale).Height);
        // The slide went through the heights in between, both ways.
        Assert.Contains(heights, h => h > 2 * scale + 1 && h < 48 * scale - 1);
    }

    [Fact]
    public void An_auto_hiding_taskbar_stays_up_while_Start_is_open()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        bar.WriteProperty("autoHide", true);
        bar.WriteProperty("startOpen", true);
        bar.Scene.PointerMove(500, 47);
        bar.Scene.PointerLeave();
        shell.Loop.Advance(TimeSpan.FromSeconds(3));
        Assert.True((bool)bar.ReadProperty("revealed")!);
        bar.WriteProperty("startOpen", false);
        shell.Loop.Advance(TimeSpan.FromSeconds(2));
        Assert.False((bool)bar.ReadProperty("revealed")!);
    }

    [Fact]
    public void Only_the_primary_screens_taskbar_is_the_main_one()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        bar.Screen = Screen("DISPLAY2", 1920);
        shell.Settle();
        Assert.False((bool)bar.ReadProperty("isMainScreen")!);
        bar.Screen = Screen("DISPLAY1", 0);
        shell.Settle();
        Assert.True((bool)bar.ReadProperty("isMainScreen")!);
    }

    [Fact]
    public void The_tray_area_shows_the_icons_Settings_shows_and_the_chevron_for_the_rest()
    {
        var desk = new Desk();
        var shown = new FakeTrayItem("Sync", "Sync\nUp to date", false);
        var hidden = new FakeTrayItem("Updater", "Updater", true);
        desk.Services.SystemTray.Items.Add(shown);
        desk.Services.SystemTray.Items.Add(hidden);
        var (shell, bar) = Taskbar(desk);
        using var _ = shell;
        var icons = DefaultShellHarness.FindAll(bar.Scene.Root).Where(i => i.ObjectName == "trayIcon" && i.Visible).ToArray();
        Assert.Single(icons);
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        Assert.Equal("Sync", tree.Find(icons[0])!.Name);
        Assert.Equal("Show hidden icons", tree.Find(In(bar, "trayChevron"))!.Name);

        Click(shell, bar, icons[0]);
        Assert.Equal(["click"], shown.Calls);

        desk.Services.SystemTray.Items.Remove(hidden);
        shell.Settle();
        Assert.DoesNotContain(DefaultShellHarness.FindAll(bar.Scene.Root), i => i.ObjectName == "trayChevron" && i.Visible);
    }

    private const string Pins = """
        import QtQuick
        Item {
          property alias pins: pins
          property string pinNext
          property string unpinNext
          onPinNextChanged: pins.pin(pinNext)
          onUnpinNextChanged: pins.unpin(unpinNext)
          TaskbarPins { id: pins }
        }
        """;

    [Fact]
    public void Pins_are_kept_in_a_file_once_they_change_and_read_back()
    {
        using var shell = new DefaultShellHarness(Pins, services: new FakeServices());
        var path = Path.Combine(shell.Directory, "data", "taskbar.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pins = (QObject)shell.Root.ReadProperty("pins")!;
        Assert.Equal(["Microsoft.Windows.Explorer", "MSEdge", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"], Ids(pins));

        pins.WriteProperty("path", path);
        shell.Settle();
        Assert.False(File.Exists(path));
        shell.Root.WriteProperty("unpinNext", "MSEdge");
        shell.Root.WriteProperty("pinNext", @"C:\Tools\tool.exe");
        Assert.Contains("tool.exe", File.ReadAllText(path));
        Assert.DoesNotContain("MSEdge", File.ReadAllText(path));

        using var again = new DefaultShellHarness(Pins, services: new FakeServices());
        var read = (QObject)again.Root.ReadProperty("pins")!;
        read.WriteProperty("path", path);
        Assert.Equal(["Microsoft.Windows.Explorer", "Microsoft.WindowsStore_8wekyb3d8bbwe!App", @"C:\Tools\tool.exe"], Ids(read));
    }

    [Fact]
    public void A_pin_made_as_the_saved_pins_are_read_keeps_both()
    {
        using var shell = new DefaultShellHarness(Pins, services: new FakeServices());
        var path = Path.Combine(shell.Directory, "data", "taskbar.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "pinned": ["Saved.App"] }""");
        var pins = (QObject)shell.Root.ReadProperty("pins")!;

        // No turn of the loop between finding the file and the pin.
        pins.WriteProperty("path", path);
        shell.Root.WriteProperty("pinNext", "New.App");
        Assert.Equal(["Saved.App", "New.App"], Ids(pins));
        Assert.Contains("Saved.App", File.ReadAllText(path));
        Assert.Contains("New.App", File.ReadAllText(path));

        shell.Settle();
        Assert.Equal(["Saved.App", "New.App"], Ids(pins));
    }

    [Fact]
    public void A_pin_made_while_a_change_to_the_file_waits_to_be_read_is_not_undone()
    {
        using var shell = new DefaultShellHarness(Pins, services: new FakeServices());
        var path = Path.Combine(shell.Directory, "data", "taskbar.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "pinned": ["Saved.App"] }""");
        var pins = (QObject)shell.Root.ReadProperty("pins")!;
        pins.WriteProperty("path", path);
        shell.Settle();

        // Another copy of the shell changes the file; the watcher's news is still queued when this one pins.
        File.WriteAllText(path, """{ "pinned": ["Saved.App", "Other.App"] }""");
        shell.Root.WriteProperty("pinNext", "New.App");
        Thread.Sleep(200);
        shell.Settle();

        // The last write wins, and what is shown is what is in the file.
        Assert.Equal(["Saved.App", "New.App"], Ids(pins));
        Assert.Contains("New.App", File.ReadAllText(path));
    }

    [Fact]
    public void Without_a_data_folder_pins_change_but_nothing_is_written()
    {
        using var shell = new DefaultShellHarness(Pins, services: new FakeServices());
        var pins = (QObject)shell.Root.ReadProperty("pins")!;
        Assert.Equal("", pins.ReadProperty("path"));
        shell.Root.WriteProperty("pinNext", "Contoso.Notes");
        shell.Settle();
        Assert.Contains("Contoso.Notes", Ids(pins));
        Assert.False(File.Exists(Path.Combine(Path.GetPathRoot(shell.Directory)!, "taskbar.json")));
    }

    private static string[] Ids(QObject pins) => ((IEnumerable<object?>)pins.ReadProperty("ids")!).Select(o => o!.ToString()!).ToArray();

    /// <summary>The menu's items as Narrator reads them.</summary>
    private static string[] MenuLabels(QObject menu)
    {
        var scene = ((ShellWindow)menu).Scene;
        var tree = new AccessibleTree(scene);
        tree.Refresh();
        return DefaultShellHarness.FindAll(scene.Root)
            .Where(i => i.ObjectName.StartsWith("menu:", StringComparison.Ordinal))
            .OrderBy(i => i.MapToScene(0, 0).Y)
            .Select(i => tree.Find(i)!)
            .Where(n => n.Role == AccessibleAttached.RoleValue.MenuItem)
            .Select(n => n.Name)
            .ToArray();
    }

    private static void AssertNear(QColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.R8 - actual.Red), 0, 3);
        Assert.InRange(Math.Abs(expected.G8 - actual.Green), 0, 3);
        Assert.InRange(Math.Abs(expected.B8 - actual.Blue), 0, 3);
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
