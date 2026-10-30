using Jint.Native;
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
    public void A_window_that_closes_is_no_longer_kept_as_the_one_its_app_used_last()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        desk.Services.WindowList.Activate(desk.Shell2);
        shell.Settle();
        desk.Services.WindowList.Activate(desk.Documents);
        shell.Settle();
        Assert.Contains(desk.Shell2, UsedLast(shell));

        desk.Services.WindowList.Windows.Remove(desk.Shell2);
        shell.Settle();
        Assert.DoesNotContain(desk.Shell2, UsedLast(shell));
        Assert.Contains(desk.Documents, UsedLast(shell));
    }

    private static object?[] UsedLast(DefaultShellHarness shell) =>
        [.. ((JsObject)shell.Root.ReadProperty("lastUsed")!).GetOwnProperties().Select(p => p.Value.Value.ToObject())];

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

    private static void ClickIn(ShellWindow window, Item item)
    {
        var p = item.MapToScene(item.Width / 2, item.Height / 2);
        window.Scene.PointerMove(p.X, p.Y);
        window.Scene.PointerPress(p.X, p.Y, QtEnums.LeftButton);
        window.Scene.PointerRelease(p.X, p.Y, QtEnums.LeftButton);
    }

    [Theory]
    [InlineData(1920, 3, false)]
    [InlineData(1920, 9, false)]
    [InlineData(1280, 9, true)]
    [InlineData(960, 9, true)]
    [InlineData(1920, 20, true)]
    [InlineData(270, 3, true)]
    [InlineData(270, 9, true)]
    public void A_large_group_fits_its_screen_and_every_window_can_be_reached_activated_and_closed(double workWidth, int count, bool asList)
    {
        // A screen whose work area is this many logical pixels wide: 1920 physical pixels at 100%, 150% and 200%.
        var desk = new Desk();
        for (var i = 2; i < count; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"Window {i}", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        shell.Root.WriteProperty("screen", new ShellScreen(new ScreenInfo("DISPLAY1", "DISPLAY1", "", "", "", new QRect(0, 0, workWidth, 1080), new QRect(0, 0, workWidth, 1032), 1920 / workWidth, 0, 0, 60, true)));
        var preview = Popup(shell, "preview");
        Hover(shell, Button(shell, "app:microsoft.windowsterminal_8wekyb3d8bbwe!app"));
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(preview.Visible);
        Assert.True(preview.ImplicitWidth <= workWidth - 32, $"{preview.ImplicitWidth} wide on a {workWidth} work area");
        Assert.True(preview.ImplicitHeight <= 1032 * 0.6 + 16);
        preview.OnResized(preview.ImplicitWidth, preview.ImplicitHeight, 1);
        shell.Settle();

        var items = DefaultShellHarness.FindAll(preview.Scene.Root)
            .Where(i => (i.ObjectName.StartsWith("preview:", StringComparison.Ordinal) || i.ObjectName.StartsWith("previewRow:", StringComparison.Ordinal)) && i.Visible)
            .ToList();
        Assert.Equal(count, items.Count);
        var listed = items[0].ObjectName.StartsWith("previewRow:", StringComparison.Ordinal);
        // Previews while they can be 152 px or wider; a list of titles below that.
        Assert.Equal(asList, listed);
        Assert.All(items, i => Assert.True(i.MapToScene(0, 0).X >= 0 && i.MapToScene(i.Width, 0).X <= preview.ImplicitWidth));

        // The last window: scrolled to when listed, then brought forward, then closed.
        var lastWindow = (FakeAppWindow)desk.Services.WindowList.Windows.Values.Last()!;
        var last = items[^1];
        for (var i = 0; i < 40 && last.MapToScene(0, last.Height).Y > preview.ImplicitHeight; i++)
        {
            preview.Scene.Wheel(20, 20, 0, -120);
            shell.Loop.Advance(TimeSpan.FromMilliseconds(300));
        }
        Assert.True(last.MapToScene(0, last.Height).Y <= preview.ImplicitHeight);
        ClickIn(preview, last);
        shell.Settle();
        Assert.Equal(["activate"], lastWindow.Calls);

        preview.Visible = true;
        shell.Settle();
        var close = DefaultShellHarness.FindAll(last).First(i => i.ObjectName == "previewClose");
        // Every close button is inside the popup, which is inside the work area: a 270 px work area is a 1080 px
        // wide portrait screen at 400%.
        Assert.True(preview.ImplicitWidth <= workWidth);
        Assert.All(items, i => Assert.True(i.MapToScene(i.Width, 0).X <= preview.ImplicitWidth));
        var p = last.MapToScene(last.Width / 2, last.Height / 2);
        preview.Scene.PointerMove(p.X, p.Y);
        shell.Settle();
        Assert.True(close.Visible);
        ClickIn(preview, close);
        shell.Settle();
        Assert.Equal(["activate", "close"], lastWindow.Calls);
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
        // One slot pins or unpins, whichever the app is.
        Choose("app:file explorer", "pin");
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
    public void A_preview_close_button_turns_Windows_close_red_with_a_white_glyph_under_the_pointer()
    {
        var saved = Theme.Current;
        try
        {
            foreach (var dark in new[] { false, true })
            {
                Theme.Current = Theme.Default(dark);
                var (fill, glyph) = HoveredClose();
                Assert.Equal(QColor.Parse("#c42b1c"), fill);
                Assert.Equal(QColor.Parse("#ffffff"), glyph);
            }

            // A contrast theme marks it with the highlight, as it does every other control.
            var colors = Theme.Default(false).System with { Highlight = QColor.Parse("#903909"), HighlightText = QColor.Parse("#fff5e3") };
            Theme.Current = Theme.Default(false) with { System = colors, IsHighContrast = true };
            Assert.Equal((colors.Highlight, colors.HighlightText), HoveredClose());
        }
        finally
        {
            Theme.Current = saved;
        }
    }

    private static (object? Fill, object? Glyph) HoveredClose()
    {
        var desk = new Desk();
        using var shell = new DefaultShellHarness(Tasks, width: 600, height: 48, services: desk.Services);
        var preview = Popup(shell, "preview");
        Hover(shell, Button(shell, "window:tool"));
        shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(preview.Visible);
        preview.OnResized(preview.ImplicitWidth, preview.ImplicitHeight, 1);
        shell.Settle();
        var card = DefaultShellHarness.FindAll(preview.Scene.Root).Single(i => i.ObjectName == "preview:Tool - untitled");
        var p = card.MapToScene(card.Width / 2, card.Height / 2);
        preview.Scene.PointerMove(p.X, p.Y);
        shell.Settle();
        var close = DefaultShellHarness.FindAll(card).Single(i => i.ObjectName == "previewClose");
        var fill = DefaultShellHarness.FindAll(close).Single(i => i.ObjectName == "previewCloseFill");
        var glyph = DefaultShellHarness.FindAll(close).Single(i => i.ObjectName == "previewCloseGlyph");
        Assert.Equal(0, ((QColor)fill.ReadProperty("color")!).A);
        p = close.MapToScene(close.Width / 2, close.Height / 2);
        preview.Scene.PointerMove(p.X, p.Y);
        shell.Settle();
        return (fill.ReadProperty("color"), glyph.ReadProperty("color"));
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
          // Win+T, as shell.qml calls it.
          property int winT: 0
          onWinTChanged: bar.focusApps()
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

    [Theory]
    [InlineData(1280)]
    [InlineData(800)]
    public void Apps_that_do_not_fit_stop_before_the_tray_and_scroll_into_view(double width)
    {
        var desk = new Desk();
        for (var i = 0; i < 30; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"App {i}", $"app{i}", $@"C:\Apps\app{i}.exe"));
        desk.Services.SystemTray.Items.Add(new FakeTrayItem("Sync", "Sync", false));
        var (shell, bar) = Taskbar(desk, width);
        using var _ = shell;

        var strip = In(bar, "taskStrip");
        var stripLeft = strip.MapToScene(0, 0).X;
        var stripRight = stripLeft + strip.Width;
        var trayLeft = In(bar, "taskbarRight").MapToScene(0, 0).X;
        Assert.True(stripLeft >= 12);
        Assert.True(stripRight <= trayLeft - 12, $"the apps end at {stripRight}, the tray starts at {trayLeft}");

        var buttons = DefaultShellHarness.FindAll(bar.Scene.Root).Where(i => i.ObjectName.StartsWith("task:", StringComparison.Ordinal)).ToList();
        // File Explorer (pinned and open), the terminal, the tool, and the 30 apps.
        Assert.Equal(33, buttons.Count);
        // Narrator reaches every app, shown or scrolled away.
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        Assert.All(buttons, b => Assert.NotNull(tree.Find(b)));

        // The wheel scrolls through them; each is shown whole at some point.
        var seen = new HashSet<string>();
        for (var step = 0; step < 60; step++)
        {
            foreach (var b in buttons)
            {
                var left = b.MapToScene(0, 0).X;
                if (left >= stripLeft - 0.5 && left + b.Width <= stripRight + 0.5)
                    seen.Add(b.ObjectName);
            }
            bar.Scene.Wheel(stripLeft + 20, 24, 0, -120);
            shell.Loop.Advance(TimeSpan.FromMilliseconds(300));
        }
        Assert.Equal(buttons.Select(b => b.ObjectName).ToHashSet(), seen);
    }

    [Fact]
    public void While_the_centered_group_slides_after_apps_or_tray_icons_arrive_the_apps_never_reach_the_tray()
    {
        var desk = new Desk();
        desk.Services.SystemTray.Items.Add(new FakeTrayItem("Sync", "Sync", false));
        var (shell, bar) = Taskbar(desk, 1280);
        using var _ = shell;
        var strip = In(bar, "taskStrip");
        var right = In(bar, "taskbarRight");
        var frames = 0;

        void Frames()
        {
            for (var i = 0; i < 40; i++)
            {
                shell.Loop.Advance(TimeSpan.FromMilliseconds(16));
                var stripRight = strip.MapToScene(strip.Width, 0).X;
                var trayLeft = right.MapToScene(0, 0).X;
                Assert.True(stripRight <= trayLeft - 12 + 0.5, $"frame {frames}: the apps end at {stripRight}, the tray starts at {trayLeft}");
                Assert.True(strip.MapToScene(0, 0).X >= 12 + 88 - 0.5);
                frames++;
            }
        }

        // 30 apps arrive at once while the group sits in the middle.
        for (var i = 0; i < 30; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"App {i}", $"app{i}", $@"C:\Appspp{i}.exe"));
        Frames();
        // The tray grows while the apps are crowded, and shrinks again.
        for (var i = 0; i < 6; i++)
            desk.Services.SystemTray.Items.Add(new FakeTrayItem($"Tray {i}", $"Tray {i}", false));
        Frames();
        foreach (var item in desk.Services.SystemTray.Items.Values.Skip(1).ToList())
            desk.Services.SystemTray.Items.Remove((QObject)item!);
        Frames();
        // Most apps leave: the group slides back to the middle.
        foreach (var window in desk.Services.WindowList.Windows.Values.OfType<FakeAppWindow>().Where(w => w.AppId.StartsWith("app", StringComparison.Ordinal)).ToList())
            desk.Services.WindowList.Windows.Remove(window);
        Frames();
        Assert.DoesNotContain(shell.Messages, m => m.Contains("loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_few_apps_take_only_their_own_width_and_do_not_scroll()
    {
        var (shell, bar) = Taskbar(new Desk());
        using var _ = shell;
        var strip = In(bar, "taskStrip");
        // File Explorer, the terminal and the tool.
        Assert.Equal(3 * 44 - 4, strip.Width);
        Assert.False((bool)strip.ReadProperty("interactive")!);
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

    // Keyboard

    private const int KeyReturn = 0x01000004, KeySpace = 0x20, KeyEscape = 0x01000000, KeyLeft = 0x01000012, KeyRight = 0x01000014;
    private const int KeyUp = 0x01000013, KeyDown = 0x01000015, KeyMenu = 0x01000055, KeyF10 = 0x01000039, KeyTab = 0x01000001;

    private static void Key(DefaultShellHarness shell, ShellWindow window, int key, int modifiers = 0)
    {
        window.Scene.KeyPress(new KeyEvent { Key = key, Modifiers = modifiers });
        window.Scene.KeyRelease(new KeyEvent { Key = key, Modifiers = modifiers });
        shell.Settle();
    }

    private static string? Focused(ShellWindow window) => window.Scene.FocusItem?.ObjectName;

    private static (DefaultShellHarness Shell, PanelWindow Bar) KeyboardDesk(out Desk desk)
    {
        desk = new Desk();
        desk.Services.SystemTray.Items.Add(new FakeTrayItem("Sync", "Sync", false));
        return Taskbar(desk);
    }

    [Fact]
    public void Every_taskbar_button_is_a_keyboard_stop_Narrator_can_focus()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        Assert.NotNull(desk);
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        var buttons = new[] { "startButton", "searchButton", "task:app:file explorer", "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "task:window:tool", "trayIcon" }
            .Select(name => In(bar, name)).ToList();
        Assert.All(buttons, b => Assert.True(tree.Find(b)!.IsKeyboardFocusable, b.ObjectName));

        // Tab goes through them in the order they are drawn, then the quick settings button and the clock.
        var order = new List<string?>();
        In(bar, "startButton").ForceActiveFocus(QtEnums.TabFocusReason);
        for (var i = 0; i < 8; i++)
        {
            order.Add(Focused(bar));
            Key(shell, bar, KeyTab);
        }
        Assert.Equal(["startButton", "searchButton", "task:app:file explorer", "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "task:window:tool", "trayIcon"], order.Take(6));
    }

    [Fact]
    public void Win_T_puts_the_keyboard_on_the_first_app_with_a_focus_rectangle_and_Escape_gives_it_back()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        Assert.False(bar.Focusable);

        shell.Root.WriteProperty("winT", 1);
        shell.Settle();
        Assert.Equal("task:app:file explorer", Focused(bar));
        Assert.True(bar.Scene.FocusVisible);
        Assert.True((bool)bar.ReadProperty("keyboardActive")!);
        Assert.True(bar.Focusable);

        Key(shell, bar, KeyRight);
        Assert.Equal("task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app", Focused(bar));
        Key(shell, bar, KeyLeft);
        Key(shell, bar, KeyLeft);
        Assert.Equal("searchButton", Focused(bar));

        // Escape: the keyboard goes back to the window that had it, PowerShell.
        Key(shell, bar, KeyEscape);
        Assert.False((bool)bar.ReadProperty("keyboardActive")!);
        Assert.False(bar.Focusable);
        Assert.Equal(["activate"], desk.Shell1.Calls);
    }

    [Fact]
    public void Narrator_focusing_a_button_puts_the_keyboard_in_the_taskbar_until_another_window_takes_it()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        tree.Find(In(bar, "task:window:tool"))!.Focus();
        shell.Settle();
        Assert.Equal("task:window:tool", Focused(bar));
        Assert.True((bool)bar.ReadProperty("keyboardActive")!);
        Assert.True(bar.Focusable);

        desk.Services.WindowList.Activate(desk.Documents);
        shell.Settle();
        Assert.False((bool)bar.ReadProperty("keyboardActive")!);
        Assert.False(bar.Focusable);
        Assert.Empty(desk.Shell1.Calls);
    }

    [Fact]
    public void Enter_Space_Shift_Enter_and_the_menu_key_do_what_clicks_do()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        shell.Root.WriteProperty("winT", 1);
        shell.Settle();

        // Start and Search.
        In(bar, "startButton").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeySpace);
        Assert.Equal(["startButton"], ((IEnumerable<object?>)shell.Root.ReadProperty("started")!).Select(o => o?.ToString()));
        In(bar, "searchButton").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyReturn);
        Assert.Equal(1, Convert.ToInt32(shell.Root.ReadProperty("searched")));

        // An app: Enter brings its window forward, Shift+Enter opens a new one.
        In(bar, "task:window:tool").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyReturn);
        Assert.Equal(["activate"], desk.Tool.Calls);
        In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyReturn, QtEnums.ShiftModifier);
        Assert.Equal(1, desk.Terminal.Launches);

        // The menu key and Shift+F10 open its menu, with the keyboard on the first item.
        var menu = (PopupWindow)((QObject)bar.ReadProperty("taskList")!).ReadProperty("menu")!;
        Key(shell, bar, KeyMenu);
        Assert.True(menu.Visible);
        Assert.Equal("menu:launch", Focused(menu));
        Key(shell, menu, KeyEscape);
        Assert.False(menu.Visible);
        Assert.Equal("task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app", Focused(bar));
        Key(shell, bar, KeyF10, QtEnums.ShiftModifier);
        Assert.True(menu.Visible);

        // A tray icon: Enter clicks it, the menu key asks the app for its menu.
        var sync = (FakeTrayItem)desk.Services.SystemTray.Items.Values.Single()!;
        In(bar, "trayIcon").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyReturn);
        Key(shell, bar, KeyMenu);
        Assert.Equal(["click", "display"], sync.Calls);
    }

    [Fact]
    public void The_arrow_keys_move_focus_through_the_menu_so_Narrator_reads_each_item()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        In(bar, "task:app:file explorer").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyMenu);
        var menu = (PopupWindow)((QObject)bar.ReadProperty("taskList")!).ReadProperty("menu")!;
        var tree = new AccessibleTree(menu.Scene);

        string Read()
        {
            tree.Refresh();
            var node = tree.Find(menu.Scene.FocusItem!)!;
            Assert.Equal(AccessibleAttached.RoleValue.MenuItem, node.Role);
            Assert.True(node.HasKeyboardFocus);
            return node.Name;
        }

        Assert.Equal("File Explorer", Read());
        Key(shell, menu, KeyDown);
        Assert.Equal("Unpin from taskbar", Read());
        Key(shell, menu, KeyDown);
        Assert.Equal("Close window", Read());
        Key(shell, menu, KeyDown);
        Assert.Equal("File Explorer", Read());
        Key(shell, menu, KeyUp);
        Assert.Equal("Close window", Read());

        Key(shell, menu, KeyReturn);
        Assert.False(menu.Visible);
        Assert.Equal(["close"], desk.Documents.Calls);
    }

    [Fact]
    public void Up_opens_an_apps_previews_with_the_keyboard_in_them_and_Escape_goes_back_to_its_button()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        var terminal = In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        terminal.ForceActiveFocus(QtEnums.TabFocusReason);
        var preview = (PopupWindow)((QObject)bar.ReadProperty("taskList")!).ReadProperty("preview")!;

        Key(shell, bar, KeyUp);
        Assert.True(preview.Visible);
        Assert.True(preview.GrabFocus);
        Assert.Equal("preview:PowerShell", Focused(preview));
        Assert.True(preview.Scene.FocusVisible);

        // Right moves to the preview's close button, then to the next preview.
        Key(shell, preview, KeyRight);
        Assert.Equal("previewClose", Focused(preview));
        Key(shell, preview, KeyRight);
        Assert.Equal("preview:Command Prompt", Focused(preview));
        var tree = new AccessibleTree(preview.Scene);
        tree.Refresh();
        Assert.Equal("Command Prompt", tree.Find(preview.Scene.FocusItem!)!.Name);

        Key(shell, preview, KeyEscape);
        Assert.False(preview.Visible);
        Assert.False(preview.GrabFocus);
        Assert.Same(terminal, bar.Scene.FocusItem);

        // Enter on a preview brings that window forward.
        Key(shell, bar, KeyUp);
        Key(shell, preview, KeyRight);
        Key(shell, preview, KeyRight);
        Key(shell, preview, KeyReturn);
        Assert.Equal(["activate"], desk.Shell2.Calls);
        Assert.False(preview.Visible);
    }

    private static PopupWindow TaskPopup(PanelWindow bar, string name) => (PopupWindow)((QObject)bar.ReadProperty("taskList")!).ReadProperty(name)!;

    /// <summary>The focused item of a window, as Narrator would read it; never a destroyed one.</summary>
    private static string FocusedName(ShellWindow window)
    {
        var item = window.Scene.FocusItem;
        Assert.NotNull(item);
        Assert.False(item.IsDestroyed);
        var tree = new AccessibleTree(window.Scene);
        tree.Refresh();
        var node = tree.Find(item)!;
        Assert.True(node.HasKeyboardFocus, $"{item.ObjectName} is read as {node.Item.ObjectName} ({node.Role} '{node.Name}')");
        return node.Name;
    }

    [Fact]
    public void An_open_menu_keeps_its_focused_item_while_another_app_comes_and_moves_focus_when_its_item_goes()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        In(bar, "task:app:file explorer").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyMenu);
        var menu = TaskPopup(bar, "menu");
        Key(shell, menu, KeyDown);
        Key(shell, menu, KeyDown);
        var row = menu.Scene.FocusItem;
        Assert.Equal("Close window", FocusedName(menu));

        // Another app arrives: the same row keeps focus.
        desk.Services.WindowList.Windows.Add(new FakeAppWindow("Notes", "notes", @"C:\Tools\notes.exe"));
        shell.Settle();
        Assert.Same(row, menu.Scene.FocusItem);
        Assert.Equal("Close window", FocusedName(menu));

        // File Explorer's window closes: Close goes, and focus moves to the first item, where the arrows go on.
        desk.Services.WindowList.Windows.Remove(desk.Documents);
        shell.Settle();
        Assert.True(menu.Visible);
        Assert.Equal("File Explorer", FocusedName(menu));
        Key(shell, menu, KeyDown);
        Assert.Equal("Unpin from taskbar", FocusedName(menu));
        Key(shell, menu, KeyEscape);
        Assert.False(menu.Visible);
        Assert.Equal("task:app:file explorer", Focused(bar));
    }

    [Fact]
    public void Closing_the_focused_preview_moves_focus_to_the_next_and_the_last_one_back_to_the_button()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        var terminal = In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        terminal.ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Assert.Equal("PowerShell", FocusedName(preview));

        // Delete closes it; the window goes from the list, and focus moves to the one left.
        Key(shell, preview, 0x01000007);
        Assert.Equal(["close"], desk.Shell1.Calls);
        desk.Services.WindowList.Windows.Remove(desk.Shell1);
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.Equal("Command Prompt", FocusedName(preview));
        Key(shell, preview, KeyReturn);
        Assert.Equal(["activate"], desk.Shell2.Calls);

        // The last window closes while its preview has the keyboard: the button gets it back.
        Key(shell, bar, KeyUp);
        desk.Services.WindowList.Windows.Remove(desk.Shell2);
        shell.Settle();
        Assert.False(preview.Visible);
    }

    [Fact]
    public void A_large_group_from_the_keyboard_is_a_list_whose_focused_row_scrolls_into_view_and_can_be_activated_and_closed()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        for (var i = 2; i < 30; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"Window {i}", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        shell.Settle();
        In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Assert.Equal("PowerShell", FocusedName(preview));
        Assert.StartsWith("previewRow:", preview.Scene.FocusItem!.ObjectName, StringComparison.Ordinal);
        var list = DefaultShellHarness.FindAll(preview.Scene.Root).First(i => i.ObjectName == "previewList");

        // Down goes row by row, past the close buttons, and each row it reaches is shown whole.
        for (var i = 0; i < 29; i++)
        {
            Key(shell, preview, KeyDown);
            var row = preview.Scene.FocusItem!;
            Assert.StartsWith("previewRow:", row.ObjectName, StringComparison.Ordinal);
            var top = row.MapToScene(0, 0).Y - list.MapToScene(0, 0).Y;
            Assert.True(top >= -0.5 && top + row.Height <= list.Height + 0.5, $"{row.ObjectName} at {top} in a list {list.Height} high");
        }
        Assert.Equal("Window 29", FocusedName(preview));

        // Tab reaches its close button; Enter on it closes the window.
        Key(shell, preview, KeyTab);
        Assert.Equal("Close Window 29", FocusedName(preview));
        Key(shell, preview, KeyReturn);
        var last = (FakeAppWindow)desk.Services.WindowList.Windows.Values.Last()!;
        Assert.Equal(["close"], last.Calls);

        // Up goes back to the row, then to the one above; Enter brings its window forward.
        Key(shell, preview, KeyUp);
        Assert.Equal("Window 29", FocusedName(preview));
        Key(shell, preview, KeyUp);
        Assert.Equal("Window 28", FocusedName(preview));
        Key(shell, preview, KeyReturn);
        Assert.Equal(["activate"], ((FakeAppWindow)desk.Services.WindowList.Windows.Values[^2]!).Calls);
    }

    [Fact]
    public void A_group_that_becomes_a_list_keeps_the_keyboard_on_the_same_window()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        // A 1280 px screen: three windows are previews, nine are a list.
        bar.Screen = new ShellScreen(new ScreenInfo("DISPLAY1", "DISPLAY1", "", "", "", new QRect(0, 0, 1280, 1080), new QRect(0, 0, 1280, 1032), 1, 0, 0, 60, true));
        desk.Services.WindowList.Windows.Add(new FakeAppWindow("Window 2", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        shell.Settle();
        In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Key(shell, preview, KeyRight);
        Key(shell, preview, KeyRight);
        Assert.Equal("Command Prompt", FocusedName(preview));
        Assert.StartsWith("preview:", preview.Scene.FocusItem!.ObjectName, StringComparison.Ordinal);

        for (var i = 3; i < 9; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"Window {i}", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        shell.Settle();
        Assert.Equal("Command Prompt", FocusedName(preview));
        Assert.StartsWith("previewRow:", preview.Scene.FocusItem!.ObjectName, StringComparison.Ordinal);

        // And back to previews when the group shrinks.
        foreach (var window in desk.Services.WindowList.Windows.Values.OfType<FakeAppWindow>().Where(w => w.Title.StartsWith("Window ", StringComparison.Ordinal)).ToList())
            desk.Services.WindowList.Windows.Remove(window);
        shell.Settle();
        Assert.Equal("Command Prompt", FocusedName(preview));
        Assert.StartsWith("preview:", preview.Scene.FocusItem!.ObjectName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(800)]
    [InlineData(1280)]
    public void The_app_with_keyboard_or_Narrator_focus_scrolls_into_view(double width)
    {
        var desk = new Desk();
        for (var i = 0; i < 30; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"App {i}", $"app{i}", $@"C:\Apps\app{i}.exe"));
        var (shell, bar) = Taskbar(desk, width);
        using var _ = shell;
        var strip = In(bar, "taskStrip");

        void Shown(Item button)
        {
            shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
            var left = button.MapToScene(0, 0).X - strip.MapToScene(0, 0).X;
            Assert.True(left >= -0.5 && left + button.Width <= strip.Width + 0.5, $"{button.ObjectName} at {left} in a strip {strip.Width} wide");
        }

        var buttons = DefaultShellHarness.FindAll(bar.Scene.Root).Where(i => i.ObjectName.StartsWith("task:", StringComparison.Ordinal)).OrderBy(i => i.X).ToList();
        // The last app, by Tab; the first again, by Right around the end; then the last again, by Narrator.
        buttons[^1].ForceActiveFocus(QtEnums.TabFocusReason);
        Shown(buttons[^1]);
        buttons[0].ForceActiveFocus(QtEnums.TabFocusReason);
        Shown(buttons[0]);
        Key(shell, bar, KeyRight);
        Shown(buttons[1]);
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        tree.Find(buttons[^1])!.Focus();
        Shown(buttons[^1]);

        // A narrower taskbar keeps it in view.
        bar.OnResized(width - 200, 48, 1);
        Shown(buttons[^1]);
    }

    private static void MovePointer(ShellWindow window, double x, double y)
    {
        window.Scene.PointerMove(x, y);
    }

    [Fact]
    public void The_pointer_leaving_does_not_close_previews_the_keyboard_has_open()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        var terminal = In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        var at = terminal.MapToScene(terminal.Width / 2, terminal.Height / 2);
        MovePointer(bar, at.X, at.Y);
        shell.Settle();
        terminal.ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Assert.True(preview.Visible);
        Assert.True(preview.GrabFocus);
        Assert.Equal("PowerShell", FocusedName(preview));

        // Off the button, and not into the popup. The hover timer would have closed it.
        MovePointer(bar, 1, 1);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(400));
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.True(preview.GrabFocus);
        Assert.Equal("PowerShell", FocusedName(preview));

        // Onto a card, then off the popup, while Right is moving between the cards.
        var card = preview.Scene.FocusItem!;
        var onCard = card.MapToScene(card.Width / 2, card.Height / 2);
        MovePointer(preview, onCard.X, onCard.Y);
        shell.Settle();
        Key(shell, preview, KeyRight);
        Key(shell, preview, KeyRight);
        Assert.Equal("Command Prompt", FocusedName(preview));
        MovePointer(preview, -30, -30);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(400));
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.Equal("Command Prompt", FocusedName(preview));

        // A list, the same way: the pointer leaves the button and the rows, and Down still moves.
        for (var i = 2; i < 30; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"Window {i}", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));
        shell.Settle();
        Assert.StartsWith("previewRow:", preview.Scene.FocusItem!.ObjectName, StringComparison.Ordinal);
        MovePointer(bar, 1, 1);
        var row = preview.Scene.FocusItem!;
        var onRow = row.MapToScene(row.Width / 2, row.Height / 2);
        MovePointer(preview, onRow.X, onRow.Y);
        shell.Settle();
        MovePointer(preview, -30, -30);
        shell.Loop.Advance(TimeSpan.FromMilliseconds(400));
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.True(preview.GrabFocus);
        Key(shell, preview, KeyDown);
        Assert.StartsWith("previewRow:", preview.Scene.FocusItem!.ObjectName, StringComparison.Ordinal);
        Assert.False(preview.Scene.FocusItem!.IsDestroyed);

        // Escape still comes back to the button. The pointer never dismissed it.
        Key(shell, preview, KeyEscape);
        Assert.False(preview.Visible);
        Assert.False(preview.GrabFocus);
        Assert.Same(terminal, bar.Scene.FocusItem);
    }

    [Fact]
    public void Hovering_another_button_does_not_retarget_previews_the_keyboard_has_open()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        Assert.NotNull(desk);
        var terminal = In(bar, "task:app:microsoft.windowsterminal_8wekyb3d8bbwe!app");
        terminal.ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Assert.True(preview.Visible);
        Assert.True(preview.GrabFocus);
        Assert.Equal("PowerShell", FocusedName(preview));
        Assert.Equal("app:microsoft.windowsterminal_8wekyb3d8bbwe!app", preview.ReadProperty("taskKey"));

        // Onto another app, then off the bar. The hover that would have moved the previews there does not.
        var tool = In(bar, "task:window:tool");
        var at = tool.MapToScene(tool.Width / 2, tool.Height / 2);
        MovePointer(bar, at.X, at.Y);
        shell.Settle();
        MovePointer(bar, 1, 1);
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        shell.Settle();
        Assert.True(preview.Visible);
        Assert.True(preview.GrabFocus);
        Assert.Equal("PowerShell", FocusedName(preview));
        Assert.Equal("app:microsoft.windowsterminal_8wekyb3d8bbwe!app", preview.ReadProperty("taskKey"));

        Key(shell, preview, KeyEscape);
        Assert.False(preview.Visible);
        Assert.Same(terminal, bar.Scene.FocusItem);
    }

    /// <summary>The taskbar's focused item is on screen and Narrator can read it.</summary>
    private static void AssertKeyboardTarget(PanelWindow bar)
    {
        var item = bar.Scene.FocusItem;
        Assert.NotNull(item);
        Assert.False(item.IsDestroyed);
        Assert.True(item.Visible);
        var tree = new AccessibleTree(bar.Scene);
        tree.Refresh();
        var node = tree.Find(item);
        Assert.NotNull(node);
        Assert.True(node.HasKeyboardFocus, item.ObjectName);
        Assert.True(node.IsKeyboardFocusable);
    }

    [Fact]
    public void Closing_the_last_window_of_an_unpinned_app_moves_the_keyboard_to_a_button_that_stays()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        var tool = In(bar, "task:window:tool");
        tool.ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Assert.Equal("Tool - untitled", FocusedName(preview));

        desk.Services.WindowList.Windows.Remove(desk.Tool);
        shell.Settle();
        Assert.False(preview.Visible);
        Assert.False(preview.GrabFocus);
        Assert.True((bool)bar.ReadProperty("keyboardActive")!);
        AssertKeyboardTarget(bar);
        Assert.NotEqual("task:window:tool", bar.Scene.FocusItem!.ObjectName);
        var landed = bar.Scene.FocusItem;
        Key(shell, bar, KeyRight);
        Assert.NotSame(landed, bar.Scene.FocusItem);
        AssertKeyboardTarget(bar);
        Key(shell, bar, KeyEscape);
        Assert.False((bool)bar.ReadProperty("keyboardActive")!);

        // The menu, the same way. The tool is open again, and it is still not pinned.
        desk.Services.WindowList.Windows.Add(desk.Tool);
        shell.Settle();
        tool = In(bar, "task:window:tool");
        tool.ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyMenu);
        var menu = TaskPopup(bar, "menu");
        Assert.Equal("Tool - untitled", FocusedName(menu));
        desk.Services.WindowList.Windows.Remove(desk.Tool);
        shell.Settle();
        Assert.False(menu.Visible);
        Assert.True((bool)bar.ReadProperty("keyboardActive")!);
        AssertKeyboardTarget(bar);
        Assert.NotEqual("task:window:tool", bar.Scene.FocusItem!.ObjectName);
        landed = bar.Scene.FocusItem;
        Key(shell, bar, KeyRight);
        Assert.NotSame(landed, bar.Scene.FocusItem);
        Key(shell, bar, KeyEscape);
        Assert.False((bool)bar.ReadProperty("keyboardActive")!);
    }

    [Fact]
    public void Closing_the_only_app_from_the_keyboard_leaves_Escape_a_live_target()
    {
        var (shell, bar) = KeyboardDesk(out var desk);
        using var _ = shell;
        foreach (var window in desk.Services.WindowList.Windows.Values.ToList())
        {
            if (!ReferenceEquals(window, desk.Tool))
                desk.Services.WindowList.Windows.Remove(window);
        }
        var pins = (QObject)shell.Root.ReadProperty("pins")!;
        shell.Engine.Js.CallFunction(pins.GetFunction("unpin")!, shell.Engine.Js.Wrap(pins), ["Microsoft.Windows.Explorer"]);
        shell.Settle();
        Assert.Equal(["task:window:tool"], DefaultShellHarness.FindAll(bar.Scene.Root).Where(i => i.ObjectName.StartsWith("task:", StringComparison.Ordinal) && i.Visible).Select(i => i.ObjectName));

        In(bar, "task:window:tool").ForceActiveFocus(QtEnums.TabFocusReason);
        Key(shell, bar, KeyUp);
        var preview = TaskPopup(bar, "preview");
        Assert.True(preview.GrabFocus);
        desk.Services.WindowList.Windows.Remove(desk.Tool);
        shell.Settle();
        Assert.False(preview.Visible);
        Assert.True((bool)bar.ReadProperty("keyboardActive")!);
        AssertKeyboardTarget(bar);
        Assert.False(bar.Scene.FocusItem!.ObjectName.StartsWith("task:", StringComparison.Ordinal));
        Key(shell, bar, KeyEscape);
        Assert.False((bool)bar.ReadProperty("keyboardActive")!);
        Assert.False(bar.Focusable);
    }

    [Fact]
    public void Pinning_unpinning_or_reordering_a_focused_app_keeps_its_button_in_view()
    {
        var desk = new Desk();
        for (var i = 0; i < 30; i++)
            desk.Services.WindowList.Windows.Add(new FakeAppWindow($"App {i}", $"app{i}", $@"C:\Apps\app{i}.exe"));
        var (shell, bar) = Taskbar(desk, 800);
        using var _ = shell;
        var strip = In(bar, "taskStrip");
        var list = (Item)bar.ReadProperty("taskList")!;

        void Shown(Item button)
        {
            shell.Loop.Advance(TimeSpan.FromMilliseconds(500));
            Assert.False(button.IsDestroyed);
            Assert.Same(button, bar.Scene.FocusItem);
            var left = button.MapToScene(0, 0).X - strip.MapToScene(0, 0).X;
            Assert.True(left >= -0.5 && left + button.Width <= strip.Width + 0.5, $"{button.ObjectName} at {left} in a strip {strip.Width} wide");
        }

        var last = DefaultShellHarness.FindAll(bar.Scene.Root).First(i => i.ObjectName == "task:window:app29");
        last.ForceActiveFocus(QtEnums.TabFocusReason);
        Shown(last);

        list.Emit("pinRequested", "app29");
        shell.Settle();
        Shown(last);

        list.Emit("unpinRequested", "app29");
        shell.Settle();
        Shown(last);

        // Pinning the apps in front of it rebuilds the order around the same button.
        for (var i = 0; i < 20; i++)
            list.Emit("pinRequested", $"app{i}");
        shell.Settle();
        list.Emit("pinRequested", "app29");
        shell.Settle();
        Shown(last);
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
