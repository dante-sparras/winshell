using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TasksTests
{
    private const string ExplorerPin = "Microsoft.Windows.Explorer";
    private const string StorePin = "Microsoft.WindowsStore_8wekyb3d8bbwe!App";
    private const string TerminalPin = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const string Portable = @"C:\Tools\portable.exe";

    private sealed record App(string Id, string Name, string Icon, string AppId) : ITaskApp;

    private sealed record Window(string AppId, string Executable, string Title, string Icon, bool Activated, bool Minimized) : ITaskWindow;

    private static readonly App Explorer = new("File Explorer", "File Explorer", "explorer-icon", ExplorerPin);
    private static readonly App Store = new(StorePin, "Microsoft Store", "store-icon", StorePin);
    private static readonly App Terminal = new(TerminalPin, "Terminal", "terminal-icon", TerminalPin);

    private static Window Win(string title, string appId, string executable, string icon = "", bool activated = false, bool minimized = false) =>
        new(appId, executable, title, icon, activated, minimized);

    /// <summary>By id, then by the app id a window carries, then by name, as <c>Apps.find</c> looks.</summary>
    private static Func<string, ITaskApp?> Index(params App[] apps) => name =>
        apps.FirstOrDefault(app => string.Equals(app.Id, name, StringComparison.OrdinalIgnoreCase))
        ?? apps.FirstOrDefault(app => string.Equals(app.AppId, name, StringComparison.OrdinalIgnoreCase))
        ?? apps.FirstOrDefault(app => string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase));

    private static TaskItem Bare(string pin = "", App? entry = null, params Window[] windows) =>
        new("task", pin, entry, windows, "", pin);

    private static IReadOnlyList<TaskItem> Desk()
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        var shell1 = Win("PowerShell", TerminalPin, @"C:\Program Files\WindowsApps\Terminal\WindowsTerminal.exe");
        var shell2 = Win("Command Prompt", TerminalPin, @"C:\Program Files\WindowsApps\Terminal\WindowsTerminal.exe");
        var tool = Win("Tool - untitled", "tool", @"C:\Tools\tool.exe");
        return Tasks.Build([documents, shell1, shell2, tool], [ExplorerPin, StorePin, Portable], Index(Explorer, Store, Terminal), static _ => true, true);
    }

    [Theory]
    [InlineData(@"C:\Tools\Tool.EXE", "tool")]
    [InlineData("C:/Tools/Tool.EXE", "tool")]
    [InlineData(@"\\server\share\App.EXE", "app")]
    [InlineData("notepad", "notepad")]
    [InlineData(".gitignore", ".gitignore")]
    [InlineData("archive.tar.gz", "archive.tar")]
    [InlineData("", "")]
    public void A_program_id_is_the_file_name_without_its_folder_or_extension(string path, string id) =>
        Assert.Equal(id, Tasks.ProgramId(path));

    [Theory]
    [InlineData(@"C:\Tools\tool.exe", true)]
    [InlineData("c:/Tools/tool.exe", true)]
    [InlineData(@"\\server\share\tool.exe", true)]
    [InlineData("Contoso.Notes", false)]
    [InlineData("C:tool.exe", false)]
    [InlineData("", false)]
    public void A_program_path_is_a_drive_letter_or_a_UNC_path(string pin, bool path) =>
        Assert.Equal(path, Tasks.IsProgramPath(pin));

    [Fact]
    public void Pins_come_first_in_pin_order_then_running_apps_in_the_order_they_opened()
    {
        var tasks = Desk();

        Assert.Equal(
            ["app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app", "window:portable", "app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "window:tool"],
            tasks.Select(task => task.Key));
        Assert.Equal("Documents", Assert.Single(tasks[0].Windows).Title);
        Assert.Empty(tasks[1].Windows);
        Assert.Empty(tasks[2].Windows);
        Assert.Equal(["PowerShell", "Command Prompt"], tasks[3].Windows.Select(window => window.Title));
        Assert.Equal("Tool - untitled", Assert.Single(tasks[4].Windows).Title);
    }

    [Fact]
    public void Narrator_reads_each_app_with_its_windows() =>
        Assert.Equal(
            ["File Explorer - 1 running window", "Microsoft Store pinned", "portable pinned", "Terminal - 2 running windows", "Tool - untitled - 1 running window"],
            Desk().Select(Tasks.AccessibleName));

    [Fact]
    public void A_window_is_grouped_onto_its_pinned_app()
    {
        var documents = Win("Documents", "microsoft.windows.explorer", @"C:\Windows\explorer.exe");

        var task = Assert.Single(Tasks.Build([documents], [ExplorerPin], Index(Explorer), static _ => true, true));

        Assert.Equal("app:file explorer", task.Key);
        Assert.Same(Explorer, task.Entry);
        Assert.Same(documents, Assert.Single(task.Windows));
    }

    [Fact]
    public void Two_pins_that_resolve_to_the_same_app_give_one_button()
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");

        var tasks = Tasks.Build([documents], [ExplorerPin, "File Explorer", StorePin], Index(Explorer, Store), static _ => true, true);

        Assert.Equal(["app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app"], tasks.Select(task => task.Key));
        Assert.Equal(ExplorerPin, tasks[0].Pin);
        Assert.Same(documents, Assert.Single(tasks[0].Windows));
    }

    [Fact]
    public void A_window_finds_its_index_entry_through_its_AppId()
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");

        var task = Assert.Single(Tasks.Build([documents], [], Index(Explorer), static _ => true, true));

        Assert.Equal("app:file explorer", task.Key);
        Assert.Same(Explorer, task.Entry);
        Assert.False(task.Pinned);
        Assert.Equal("File Explorer", task.PinId);
    }

    [Fact]
    public void A_window_with_no_AppId_is_keyed_by_its_program_name()
    {
        var tool = Win("Tool - untitled", "", @"C:\Tools\tool.exe");

        var task = Assert.Single(Tasks.Build([tool], [], Index(), static _ => true, true));

        Assert.Equal("window:tool", task.Key);
        Assert.Null(task.Entry);
    }

    [Fact]
    public void A_window_whose_AppId_the_index_does_not_have_is_keyed_by_that_id()
    {
        var mail = Win("Mail", "Contoso.Mail", @"C:\Tools\mail.exe");

        var task = Assert.Single(Tasks.Build([mail], [], Index(), static _ => true, true));

        Assert.Equal("window:contoso.mail", task.Key);
    }

    [Fact]
    public void A_program_path_pin_matches_its_running_programs_windows()
    {
        var window = Win("Portable", "", @"D:\Other\Portable.EXE", "icon");

        var task = Assert.Single(Tasks.Build([window], [Portable], Index(), static _ => true, true));

        Assert.Equal("window:portable", task.Key);
        Assert.Equal(Portable, task.Pin);
        Assert.Same(window, Assert.Single(task.Windows));
    }

    [Fact]
    public void A_pin_the_index_does_not_have_is_hidden_unless_it_is_a_program_path()
    {
        var tasks = Tasks.Build([], ["Contoso.Notes", Portable], Index(), static _ => true, true);

        var task = Assert.Single(tasks);
        Assert.Equal("window:portable", task.Key);
        Assert.Equal(Portable, task.Icon);
        Assert.Equal(Portable, task.PinId);
    }

    [Fact]
    public void A_hidden_pin_is_shown_once_a_window_arrives()
    {
        var window = Win("Notes", "Contoso.Notes", @"C:\Notes\notes.exe");

        var task = Assert.Single(Tasks.Build([window], ["Contoso.Notes"], Index(), static _ => true, true));

        Assert.Equal("window:contoso.notes", task.Key);
        Assert.Same(window, Assert.Single(task.Windows));
    }

    [Fact]
    public void A_pin_appears_when_the_index_gains_its_app()
    {
        Assert.Empty(Tasks.Build([], ["Contoso.Notes"], Index(), static _ => true, true));
        var notes = new App("Contoso.Notes", "Notes", "notes-icon", "Contoso.Notes");

        var task = Assert.Single(Tasks.Build([], ["Contoso.Notes"], Index(notes), static _ => true, true));

        Assert.Equal("app:contoso.notes", task.Key);
        Assert.Equal("Notes", task.Entry!.Name);
        Assert.Empty(task.Windows);
    }

    [Fact]
    public void ShowPins_false_hides_pins_with_no_shown_window_and_keeps_those_with_one()
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        var tool = Win("Tool", "tool", @"C:\Tools\tool.exe");
        string[] pins = [ExplorerPin, StorePin, Portable];

        var shown = Tasks.Build([documents, tool], pins, Index(Explorer, Store), static _ => true, false);

        Assert.Equal(["app:file explorer", "window:tool"], shown.Select(task => task.Key));
        Assert.Same(documents, Assert.Single(shown[0].Windows));

        var filtered = Tasks.Build([documents, tool], pins, Index(Explorer, Store), window => !ReferenceEquals(window, documents), false);
        Assert.Equal(["window:tool"], filtered.Select(task => task.Key));
    }

    [Fact]
    public void A_window_shows_leaves_out_is_not_on_the_button()
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        var tool = Win("Tool", "tool", @"C:\Tools\tool.exe");

        var tasks = Tasks.Build([documents, tool], [ExplorerPin, StorePin, Portable], Index(Explorer, Store), window => !ReferenceEquals(window, documents), true);

        Assert.Equal(
            ["app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app", "window:portable", "window:tool"],
            tasks.Select(task => task.Key));
        Assert.Empty(tasks[0].Windows);
        Assert.Same(tool, Assert.Single(tasks[3].Windows));
    }

    [Theory]
    [InlineData(true, true, new[] { "app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app", "window:portable", "app:microsoft.windowsterminal_8wekyb3d8bbwe!app", "window:tool" })]
    [InlineData(false, true, new[] { "app:file explorer", "app:microsoft.windowsstore_8wekyb3d8bbwe!app", "window:portable", "window:tool" })]
    [InlineData(false, false, new[] { "window:tool" })]
    public void With_several_screens_a_taskbar_shows_the_windows_and_pins_its_setting_asks_for(bool allScreens, bool showPins, string[] keys)
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        var shell1 = Win("PowerShell", TerminalPin, @"C:\Terminal\wt.exe");
        var shell2 = Win("Command Prompt", TerminalPin, @"C:\Terminal\wt.exe");
        var tool = Win("Tool", "tool", @"C:\Tools\tool.exe");

        var tasks = Tasks.Build(
            [documents, shell1, shell2, tool],
            [ExplorerPin, StorePin, Portable],
            Index(Explorer, Store, Terminal),
            window => allScreens || ReferenceEquals(window, tool),
            showPins);

        Assert.Equal(keys, tasks.Select(task => task.Key));
    }

    [Fact]
    public void An_unpinned_app_with_no_windows_goes_and_a_pinned_one_stays()
    {
        var shell1 = Win("PowerShell", TerminalPin, @"C:\Terminal\wt.exe");
        var shell2 = Win("Command Prompt", TerminalPin, @"C:\Terminal\wt.exe");

        var tasks = Tasks.Build([shell1, shell2], [ExplorerPin, StorePin, Portable], Index(Explorer, Store, Terminal), static _ => true, true);

        Assert.DoesNotContain(tasks, task => task.Key == "window:tool");
        Assert.Equal("File Explorer pinned", Tasks.AccessibleName(tasks[0]));
    }

    [Fact]
    public void Icon_and_PinId_fall_back_from_the_entry_to_the_window_to_the_pin()
    {
        var documents = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe", "doc-icon");
        var pinned = Assert.Single(Tasks.Build([documents], [ExplorerPin], Index(Explorer), static _ => true, true));
        Assert.Equal("explorer-icon", pinned.Icon);
        Assert.Equal(ExplorerPin, pinned.PinId);

        var shell = Win("PowerShell", TerminalPin, @"C:\Terminal\wt.exe", "wt-icon");
        var running = Assert.Single(Tasks.Build([shell], [], Index(Terminal), static _ => true, true));
        Assert.Equal("terminal-icon", running.Icon);
        Assert.Equal(TerminalPin, running.PinId);

        var tool = Win("Tool", "", @"C:\Tools\tool.exe", "tool-icon");
        var plain = Assert.Single(Tasks.Build([tool], [], Index(), static _ => true, true));
        Assert.Equal("tool-icon", plain.Icon);
        Assert.Equal(@"C:\Tools\tool.exe", plain.PinId);

        var path = Assert.Single(Tasks.Build(Array.Empty<Window>(), [Portable], Index(), static _ => true, true));
        Assert.Equal(Portable, path.Icon);
        Assert.Equal(Portable, path.PinId);

        var blank = new App("", "Notes", "", "Contoso.Notes");
        var notes = Win("Notes", "Contoso.Notes", @"C:\Notes\notes.exe", "window-icon");
        var entry = Assert.Single(Tasks.Build([notes], [], Index(blank), static _ => true, true));
        Assert.Equal("", entry.Icon);
        Assert.Equal("", entry.PinId);
    }

    [Fact]
    public void Name_reads_the_entry_then_one_windows_title_then_the_program()
    {
        var one = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        var two = Win("Pictures", ExplorerPin, @"C:\Windows\explorer.exe");
        Assert.Equal("File Explorer", Tasks.Name(Bare(entry: Explorer, windows: [one, two])));
        Assert.Equal("Documents", Tasks.Name(Bare(windows: [one])));
        Assert.Equal("explorer", Tasks.Name(Bare(windows: [one, two])));

        var mail = Win("Mail", "Contoso.Mail", "");
        var other = Win("Other", "Contoso.Mail", "");
        Assert.Equal("contoso", Tasks.Name(Bare(windows: [mail, other])));
        Assert.Equal("portable", Tasks.Name(Bare(pin: Portable)));
    }

    [Fact]
    public void AccessibleName_says_pinned_or_how_many_windows_are_running()
    {
        var one = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        var two = Win("Pictures", ExplorerPin, @"C:\Windows\explorer.exe");
        Assert.Equal("portable pinned", Tasks.AccessibleName(Bare(pin: Portable)));
        Assert.Equal("Documents - 1 running window", Tasks.AccessibleName(Bare(windows: [one])));
        Assert.Equal("explorer - 2 running windows", Tasks.AccessibleName(Bare(windows: [one, two])));
        Assert.Equal("File Explorer - 1 running window", Tasks.AccessibleName(Bare(entry: Explorer, windows: [one])));
    }

    [Fact]
    public void AppUserModelId_reads_the_entry_then_the_first_window()
    {
        Assert.Equal("", Tasks.AppUserModelId(null));
        Assert.Equal(ExplorerPin, Tasks.AppUserModelId(Bare(entry: Explorer, windows: [Win("Documents", "Other.App", "")])));
        Assert.Equal("From.Window", Tasks.AppUserModelId(Bare(entry: new App("id", "Name", "icon", ""), windows: [Win("One", "From.Window", ""), Win("Two", "Second.App", "")])));
        Assert.Equal("From.Window", Tasks.AppUserModelId(Bare(windows: [Win("One", "From.Window", @"C:\a.exe"), Win("Two", "Second.App", "")])));
        Assert.Equal("", Tasks.AppUserModelId(Bare(windows: [Win("Tool", "", @"C:\Tools\tool.exe")])));
        Assert.Equal("", Tasks.AppUserModelId(Bare()));
    }

    [Fact]
    public void Click_launches_an_app_that_is_not_running_and_minimizes_its_only_window_when_it_is_in_front()
    {
        Assert.IsType<TaskClick.Launch>(Tasks.Click(Bare(), null));

        var window = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe");
        Assert.Same(window, Activate(Bare(windows: [window]), null));

        var front = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe", activated: true);
        var minimize = Assert.IsType<TaskClick.Minimize>(Tasks.Click(Bare(windows: [front]), null));
        Assert.Same(front, minimize.Window);

        var minimized = Win("Documents", ExplorerPin, @"C:\Windows\explorer.exe", activated: true, minimized: true);
        Assert.Same(minimized, Activate(Bare(windows: [minimized]), null));
    }

    [Fact]
    public void Click_on_several_windows_goes_to_the_next_one_and_wraps_from_the_last_to_the_first()
    {
        var first = Win("PowerShell", TerminalPin, @"C:\Terminal\wt.exe");
        var middle = Win("Command Prompt", TerminalPin, @"C:\Terminal\wt.exe", activated: true);
        var last = Win("Azure", TerminalPin, @"C:\Terminal\wt.exe");
        Assert.Same(last, Activate(Bare(windows: [first, middle, last]), null));

        var lastFront = Win("Azure", TerminalPin, @"C:\Terminal\wt.exe", activated: true);
        var idle = Win("Command Prompt", TerminalPin, @"C:\Terminal\wt.exe");
        Assert.Same(first, Activate(Bare(windows: [first, idle, lastFront]), null));
    }

    [Fact]
    public void Click_goes_to_the_window_used_last_when_none_is_in_front()
    {
        var first = Win("PowerShell", TerminalPin, @"C:\Terminal\wt.exe");
        var second = Win("Command Prompt", TerminalPin, @"C:\Terminal\wt.exe");
        var third = Win("Azure", TerminalPin, @"C:\Terminal\wt.exe");
        var task = Bare(windows: [first, second, third]);

        Assert.Same(second, Activate(task, second));
        Assert.Same(first, Activate(task, Win("Notepad", "notepad", @"C:\Windows\notepad.exe")));
        Assert.Same(first, Activate(task, null));
    }

    [Fact]
    public void A_minimized_window_that_reports_Activated_is_not_the_one_in_front()
    {
        var minimized = Win("PowerShell", TerminalPin, @"C:\Terminal\wt.exe", activated: true, minimized: true);
        var second = Win("Command Prompt", TerminalPin, @"C:\Terminal\wt.exe");
        var third = Win("Azure", TerminalPin, @"C:\Terminal\wt.exe");

        Assert.Same(third, Activate(Bare(windows: [minimized, second, third]), third));
    }

    private static ITaskWindow Activate(TaskItem task, ITaskWindow? lastUsed) =>
        Assert.IsType<TaskClick.Activate>(Tasks.Click(task, lastUsed)).Window;
}
