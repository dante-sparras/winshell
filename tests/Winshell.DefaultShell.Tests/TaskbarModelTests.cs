using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskbarModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-taskbar-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private Preferences Open() => new(Path.Combine(_folder, Guid.NewGuid().ToString("n") + ".toml"), new ManualEventLoop());

    private TaskbarPins Pins(params string[] ids)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("n") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string[]> { ["pinned"] = ids }));
        return new TaskbarPins(path, new ManualEventLoop());
    }

    private static ScreenInfo Screen(string id, bool primary = false, double x = 0) =>
        new(id, id, "", "", "", new QRect(x, 0, 800, 600), new QRect(x, 0, 800, 560), 1, 0, 0, 60, primary);

    [Fact]
    public void Pins_come_first_and_then_the_apps_that_are_running()
    {
        using var preferences = Open();
        using var pins = Pins("explorer", "store");
        var desk = new Desk(preferences, pins);
        desk.Apps.Add(new FakeApp("explorer", "File Explorer"));
        desk.Apps.Add(new FakeApp("store", "Store"));
        desk.OpenWindows.Add(new FakeWindow { AppId = "tool", Executable = @"C:\Tools\tool.exe", Title = "Tool" });

        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        Assert.Equal(["app:explorer", "app:store", "window:tool"], model.Tasks.Select(task => task.Key).ToArray());
        Assert.Equal("File Explorer", Tasks.Name(model.Tasks[0]));
        Assert.Equal("Tool", Tasks.Name(model.Tasks[2]));
    }

    [Fact]
    public void Each_screen_shows_the_windows_its_setting_asks_for_and_one_taskbar_shows_them_all()
    {
        using var preferences = Open();
        using var pins = Pins("pinned");
        var desk = new Desk(preferences, pins);
        var main = Screen("main", primary: true);
        var other = Screen("other", x: 1920);
        var onMain = new FakeWindow { AppId = "main-app", Executable = @"C:\main.exe", Title = "Main" };
        onMain.Screens.Add(main);
        var onOther = new FakeWindow { AppId = "other-app", Executable = @"C:\other.exe", Title = "Other" };
        onOther.Screens.Add(other);
        var onBoth = new FakeWindow { AppId = "both-app", Executable = @"C:\both.exe", Title = "Both" };
        onBoth.Screens.Add(main);
        onBoth.Screens.Add(other);
        desk.OpenWindows.Add(onMain);
        desk.OpenWindows.Add(onOther);
        desk.OpenWindows.Add(onBoth);
        desk.Apps.Add(new FakeApp("pinned", "Pinned"));

        using var mainBar = desk.Bar(main, isMain: true);
        using var otherBar = desk.Bar(other, isMain: false);

        Assert.Equal(4, mainBar.Tasks.Count);
        Assert.Equal(4, otherBar.Tasks.Count);

        preferences.TaskbarAppsOn = "where";
        Assert.Equal(["pinned", "window:main-app", "window:both-app"], Keys(mainBar));
        Assert.Equal(["window:other-app", "window:both-app"], Keys(otherBar));

        preferences.TaskbarAppsOn = "main";
        Assert.Equal(["pinned", "window:main-app", "window:other-app", "window:both-app"], Keys(mainBar));
        Assert.Equal(["window:other-app", "window:both-app"], Keys(otherBar));

        preferences.TaskbarAllScreens = false;
        preferences.TaskbarAppsOn = "where";
        Assert.Equal(["pinned", "window:main-app", "window:other-app", "window:both-app"], Keys(otherBar));
    }

    [Fact]
    public void A_click_launches_minimizes_or_brings_forward_the_window_used_last()
    {
        using var preferences = Open();
        using var pins = Pins("notes");
        var desk = new Desk(preferences, pins);
        var app = new FakeApp("notes", "Notes");
        desk.Apps.Add(app);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        model.Click(model.Tasks.Single());
        Assert.Equal(1, app.Launches);

        var first = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "One" };
        var second = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Two" };
        desk.OpenWindows.Add(first);
        desk.OpenWindows.Add(second);
        desk.SetActive(second);
        model.Click(model.Tasks.Single());
        Assert.Equal(1, second.Activations);
        Assert.Equal(0, first.Activations);

        second.SetActivated(true);
        model.Click(model.Tasks.Single());
        Assert.Equal(1, first.Activations);

        desk.OpenWindows.Clear();
        var only = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Only" };
        desk.OpenWindows.Add(only);
        only.SetActivated(true);
        model.Click(model.Tasks.Single());
        Assert.Equal(1, only.Minimizations);
    }

    [Fact]
    public void A_closed_window_is_forgotten_so_the_next_click_uses_one_that_is_open()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        var first = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "One" };
        var second = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Two" };
        desk.OpenWindows.Add(first);
        desk.OpenWindows.Add(second);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        desk.SetActive(second);

        desk.OpenWindows.Remove(second);
        desk.OpenWindows.Add(second);
        model.Click(model.Tasks.Single());

        Assert.Equal(1, first.Activations);
        Assert.Equal(0, second.Activations);
    }

    [Fact]
    public void A_new_window_starts_the_app_or_the_program_the_pin_names()
    {
        using var preferences = Open();
        using var pins = Pins(@"C:\Tools\alone.exe");
        var desk = new Desk(preferences, pins);
        var app = new FakeApp("notes", "Notes", appId: "notes");
        desk.Apps.Add(app);
        var running = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Notes" };
        desk.OpenWindows.Add(running);
        var loose = new FakeWindow { AppId = "tool", Executable = @"C:\Tools\tool.exe", Title = "Tool" };
        desk.OpenWindows.Add(loose);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        model.Launch(model.Tasks.Single(task => task.Key == "app:notes"));
        Assert.Equal(1, app.Launches);
        Assert.Empty(desk.Started);

        model.Launch(model.Tasks.Single(task => task.Key == "window:tool"));
        model.Launch(model.Tasks.Single(task => task.Pin == @"C:\Tools\alone.exe"));
        var quiet = new FakeWindow { AppId = "quiet", Executable = "", Title = "Quiet" };
        desk.OpenWindows.Add(quiet);
        model.Launch(model.Tasks.Single(task => task.Key == "window:quiet"));

        Assert.Equal([@"C:\Tools\tool.exe", @"C:\Tools\alone.exe"], desk.Started);
    }

    [Fact]
    public void A_title_does_not_replace_a_button_and_focus_does_not_either()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        var window = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Old", Icon = "old" };
        desk.OpenWindows.Add(window);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        var before = model.Tasks.Single();

        window.Title = "New";
        window.Raise(nameof(ITaskWindow.Title));
        Assert.Same(before, model.Tasks.Single());
        Assert.Equal("New - 1 running window", Tasks.AccessibleName(model.Tasks.Single()));

        window.Activated = true;
        window.Raise(nameof(ITaskWindow.Activated));
        Assert.Same(before, model.Tasks.Single());
        Assert.True(TaskbarModel.Indicator.Of(model.Tasks.Single()).Active);
        Assert.Equal(16, TaskbarModel.Indicator.Of(model.Tasks.Single()).LineWidth);

        window.Minimized = true;
        window.Raise(nameof(ITaskWindow.Minimized));
        Assert.Same(before, model.Tasks.Single());
        Assert.False(TaskbarModel.Indicator.Of(model.Tasks.Single()).Active);

        window.Attention = true;
        window.Raise(nameof(IOpenWindow.Attention));
        Assert.Same(before, model.Tasks.Single());
        var waiting = TaskbarModel.Indicator.Of(model.Tasks.Single());
        Assert.True(waiting.Attention);
        Assert.False(waiting.Active);
        Assert.Equal(6, waiting.LineWidth);

        window.Icon = "new";
        window.Raise(nameof(ITaskWindow.Icon));
        Assert.NotSame(before, model.Tasks.Single());
        Assert.Equal("new", model.Tasks.Single().Icon);
    }

    [Fact]
    public void An_unchanged_rebuild_raises_no_collection_change()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        var window = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Notes", Icon = "icon" };
        window.Screens.Add(Screen("main", primary: true));
        desk.OpenWindows.Add(window);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        var changes = 0;
        model.Tasks.CollectionChanged += (_, _) => changes++;

        window.Raise(nameof(IOpenWindow.Screens));
        window.Title = "Typed";
        window.Raise(nameof(ITaskWindow.Title));

        Assert.Equal(0, changes);
        Assert.Equal("Typed - 1 running window", Tasks.AccessibleName(model.Tasks.Single()));
    }

    [Fact]
    public void A_new_window_of_an_app_replaces_only_that_apps_button()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        desk.OpenWindows.Add(new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "One" });
        var other = new FakeWindow { AppId = "other", Executable = @"C:\other.exe", Title = "Other" };
        desk.OpenWindows.Add(other);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        var notes = model.Tasks.Single(task => task.Key == "window:notes");
        var kept = model.Tasks.Single(task => task.Key == "window:other");
        var actions = new List<NotifyCollectionChangedAction>();
        model.Tasks.CollectionChanged += (_, e) => actions.Add(e.Action);

        desk.OpenWindows.Add(new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Two" });

        Assert.Equal([NotifyCollectionChangedAction.Replace], actions);
        Assert.NotSame(notes, model.Tasks.Single(task => task.Key == "window:notes"));
        Assert.Same(kept, model.Tasks.Single(task => task.Windows.Contains(other)));
        Assert.Equal(2, model.Tasks.Single(task => task.Key == "window:notes").Windows.Count);
    }

    [Fact]
    public void A_new_app_inserts_one_button_and_leaves_the_others()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        var notes = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Notes" };
        desk.OpenWindows.Add(notes);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        var kept = model.Tasks.Single();
        var actions = new List<NotifyCollectionChangedAction>();
        model.Tasks.CollectionChanged += (_, e) => actions.Add(e.Action);

        desk.OpenWindows.Add(new FakeWindow { AppId = "other", Executable = @"C:\other.exe", Title = "Other" });

        Assert.Equal([NotifyCollectionChangedAction.Add], actions);
        Assert.Same(kept, model.Tasks.Single(task => task.Windows.Contains(notes)));
        Assert.Equal(2, model.Tasks.Count);
    }

    [Fact]
    public void The_buttons_rebuild_when_pins_windows_apps_or_the_screen_filter_change()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        var window = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Notes" };
        window.Screens.Add(Screen("main", primary: true));
        desk.OpenWindows.Add(window);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        var notes = model.Tasks.Single();
        pins.Pin(@"C:\Tools\alone.exe");
        Assert.Equal(2, model.Tasks.Count);
        Assert.Same(notes, model.Tasks.Single(task => task.Windows.Contains(window)));

        desk.OpenWindows.Add(new FakeWindow { AppId = "other", Executable = @"C:\other.exe", Title = "Other" });
        Assert.Equal(3, model.Tasks.Count);

        var first = model.Tasks[0];
        desk.Apps.Add(new FakeApp("unused", "Unused"));
        Assert.Same(first, model.Tasks[0]);

        preferences.TaskbarAppsOn = "where";
        window.Screens.Clear();
        window.Screens.Add(Screen("other", x: 1920));
        window.Raise(nameof(IOpenWindow.Screens));
        Assert.DoesNotContain(model.Tasks, task => task.Windows.Contains(window));
    }

    [Fact]
    public void Start_and_Search_raise_an_event_and_open_nothing()
    {
        using var preferences = Open();
        using var pins = Pins();
        var desk = new Desk(preferences, pins);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        var starts = 0;
        var searches = 0;
        model.StartRequested += (_, _) => starts++;
        model.SearchRequested += (_, _) => searches++;

        model.RequestStart();
        model.RequestSearch();

        Assert.Equal(1, starts);
        Assert.Equal(1, searches);
        Assert.Empty(desk.Started);
        Assert.Empty(model.Tasks);
    }

    [Fact]
    public void The_running_line_is_short_until_a_window_is_in_front()
    {
        var window = new FakeWindow();
        var task = new TaskItem("k", "", null, [window], "", "");

        Assert.Equal(6, TaskbarModel.Indicator.Of(task).LineWidth);
        Assert.False(TaskbarModel.Indicator.Of(task).Active);

        window.Activated = true;
        var front = TaskbarModel.Indicator.Of(task);
        Assert.True(front.Active);
        Assert.Equal(16, front.LineWidth);

        window.Attention = true;
        var asking = TaskbarModel.Indicator.Of(task);
        Assert.True(asking.Attention);
        Assert.Equal(16, asking.LineWidth);

        window.Activated = false;
        window.Minimized = true;
        var waiting = TaskbarModel.Indicator.Of(task);
        Assert.False(waiting.Active);
        Assert.True(waiting.Attention);
        Assert.Equal(6, waiting.LineWidth);
        Assert.False(TaskbarModel.Indicator.Of(null).Running);
    }

    private static string[] Keys(TaskbarModel model) => [.. model.Tasks.Select(task => task.Entry?.Id ?? task.Key)];

    private sealed class Desk : ITaskDesktop
    {
        private readonly Preferences _preferences;
        private readonly TaskbarPins _pins;

        public Desk(Preferences preferences, TaskbarPins pins)
        {
            _preferences = preferences;
            _pins = pins;
            OpenWindows.CollectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            Apps.CollectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        public ObservableCollection<IOpenWindow> OpenWindows { get; } = [];

        public ObservableCollection<FakeApp> Apps { get; } = [];

        public IReadOnlyList<IOpenWindow> Windows => OpenWindows;

        public IOpenWindow? Active { get; private set; }

        public List<string> Started { get; } = [];

        public event EventHandler? Changed;

        public event EventHandler? ActiveChanged;

        public void SetActive(IOpenWindow? window)
        {
            Active = window;
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }

        public ITaskApp? Find(string name) =>
            Apps.FirstOrDefault(app => app.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Apps.FirstOrDefault(app => app.AppId.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Apps.FirstOrDefault(app => app.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public TaskbarModel Bar(ScreenInfo screen, bool isMain) =>
            new(this, _pins, _preferences, screen, isMain, Started.Add);
    }

    private sealed class FakeApp(string id, string name, string icon = "", string appId = "") : ITaskApp, ITaskLaunch
    {
        public string Id => id;

        public string Name { get; set; } = name;

        public string Icon { get; set; } = icon;

        public string AppId { get; set; } = appId;

        public int Launches { get; private set; }

        public void Launch() => Launches++;
    }

    private sealed class FakeWindow : IOpenWindow
    {
        public string AppId { get; set; } = "";

        public string Executable { get; set; } = "";

        public string Title { get; set; } = "";

        public string Icon { get; set; } = "";

        public bool Activated { get; set; }

        public bool Minimized { get; set; }

        public bool Attention { get; set; }

        public List<ScreenInfo> Screens { get; } = [];

        IReadOnlyList<ScreenInfo> IOpenWindow.Screens => Screens;

        public int Activations { get; private set; }

        public int Minimizations { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Activate() => Activations++;

        public void Minimize() => Minimizations++;

        public void SetActivated(bool activated)
        {
            Activated = activated;
            Raise(nameof(ITaskWindow.Activated));
        }
    }
}
