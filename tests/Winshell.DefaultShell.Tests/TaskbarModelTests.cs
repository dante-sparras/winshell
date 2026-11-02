using System.Collections.ObjectModel;
using System.ComponentModel;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskbarModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("winshell-taskbar-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private Preferences Open() => new(Path.Combine(_folder, Guid.NewGuid().ToString("n") + ".toml"), new ManualEventLoop());

    private static ScreenInfo Screen(string id, bool primary = false, double x = 0) =>
        new(id, id, "", "", "", new QRect(x, 0, 800, 600), new QRect(x, 0, 800, 560), 1, 0, 0, 60, primary);

    [Fact]
    public void Pins_come_first_and_then_the_apps_that_are_running()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
        desk.Apps.Add(new FakeApp("explorer", "File Explorer"));
        desk.Apps.Add(new FakeApp("store", "Store"));
        desk.Pins.Ids = ["explorer", "store"];
        desk.Windows.Add(new FakeWindow { AppId = "tool", Executable = @"C:\Tools\tool.exe", Title = "Tool" });

        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        Assert.Equal(["app:explorer", "app:store", "window:tool"], model.Tasks.Select(task => task.Key).ToArray());
        Assert.Equal("File Explorer", Tasks.Name(model.Tasks[0]));
        Assert.Equal("Tool", Tasks.Name(model.Tasks[2]));
    }

    [Fact]
    public void Each_screen_shows_the_windows_its_setting_asks_for_and_one_taskbar_shows_them_all()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
        var main = Screen("main", primary: true);
        var other = Screen("other", x: 1920);
        var onMain = new FakeWindow { AppId = "main-app", Executable = @"C:\main.exe", Title = "Main" };
        onMain.Screens.Add(main);
        var onOther = new FakeWindow { AppId = "other-app", Executable = @"C:\other.exe", Title = "Other" };
        onOther.Screens.Add(other);
        var onBoth = new FakeWindow { AppId = "both-app", Executable = @"C:\both.exe", Title = "Both" };
        onBoth.Screens.Add(main);
        onBoth.Screens.Add(other);
        desk.Windows.Add(onMain);
        desk.Windows.Add(onOther);
        desk.Windows.Add(onBoth);
        desk.Apps.Add(new FakeApp("pinned", "Pinned"));
        desk.Pins.Ids = ["pinned"];
        desk.Pins.Raise();

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
        var desk = new Desk(preferences);
        var app = new FakeApp("notes", "Notes");
        desk.Apps.Add(app);
        desk.Pins.Ids = ["notes"];
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        model.Click(model.Tasks.Single());
        Assert.Equal(1, app.Launches);

        var first = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "One" };
        var second = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Two" };
        desk.Windows.Add(first);
        desk.Windows.Add(second);
        desk.Active = second;
        desk.ActiveSource.Raise();
        model.Click(model.Tasks.Single());
        Assert.Equal(1, second.Activations);
        Assert.Equal(0, first.Activations);

        second.SetActivated(true);
        model.Click(model.Tasks.Single());
        Assert.Equal(1, first.Activations);

        desk.Windows.Clear();
        var only = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Only" };
        desk.Windows.Add(only);
        only.SetActivated(true);
        model.Click(model.Tasks.Single());
        Assert.Equal(1, only.Minimizations);
    }

    [Fact]
    public void A_closed_window_is_forgotten_so_the_next_click_uses_one_that_is_open()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
        var first = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "One" };
        var second = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Two" };
        desk.Windows.Add(first);
        desk.Windows.Add(second);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        desk.Active = second;
        desk.ActiveSource.Raise();

        desk.Windows.Remove(second);
        desk.Windows.Add(second);
        model.Click(model.Tasks.Single());

        Assert.Equal(1, first.Activations);
        Assert.Equal(0, second.Activations);
    }

    [Fact]
    public void A_new_window_starts_the_app_or_the_program_the_pin_names()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
        var app = new FakeApp("notes", "Notes", appId: "notes");
        desk.Apps.Add(app);
        var running = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Notes" };
        desk.Windows.Add(running);
        var loose = new FakeWindow { AppId = "tool", Executable = @"C:\Tools\tool.exe", Title = "Tool" };
        desk.Windows.Add(loose);
        desk.Pins.Ids = [@"C:\Tools\alone.exe"];
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        model.Launch(model.Tasks.Single(task => task.Key == "app:notes"));
        Assert.Equal(1, app.Launches);
        Assert.Empty(desk.Started);

        model.Launch(model.Tasks.Single(task => task.Key == "window:tool"));
        model.Launch(model.Tasks.Single(task => task.Pin == @"C:\Tools\alone.exe"));
        var quiet = new FakeWindow { AppId = "quiet", Executable = "", Title = "Quiet" };
        desk.Windows.Add(quiet);
        model.Launch(model.Tasks.Single(task => task.Key == "window:quiet"));

        Assert.Equal([@"C:\Tools\tool.exe", @"C:\Tools\alone.exe"], desk.Started);
    }

    [Fact]
    public void A_window_title_does_not_rebuild_the_buttons_and_the_states_that_move_them_do()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
        var window = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Old", Icon = "old" };
        desk.Windows.Add(window);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);
        var before = model.Tasks.Single();

        window.Title = "New";
        window.Raise(nameof(ITaskWindow.Title));
        window.Icon = "new";
        window.Raise(nameof(ITaskWindow.Icon));

        Assert.Same(before, model.Tasks.Single());
        Assert.Equal("New - 1 running window", Tasks.AccessibleName(model.Tasks.Single()));

        foreach (var name in new[]
        {
            nameof(ITaskWindow.AppId),
            nameof(ITaskWindow.Activated),
            nameof(ITaskWindow.Minimized),
            nameof(ITaskWindowView.Attention),
            nameof(ITaskWindowView.Screens),
        })
        {
            var current = model.Tasks.Single();
            window.Raise(name);
            Assert.NotSame(current, model.Tasks.Single());
        }
    }

    [Fact]
    public void The_buttons_rebuild_when_pins_windows_apps_or_the_screen_filter_change()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
        var window = new FakeWindow { AppId = "notes", Executable = @"C:\notes.exe", Title = "Notes" };
        window.Screens.Add(Screen("main", primary: true));
        desk.Windows.Add(window);
        using var model = desk.Bar(Screen("main", primary: true), isMain: true);

        var pinned = model.Tasks.Single();
        desk.Pins.Raise();
        Assert.NotSame(pinned, model.Tasks.Single());

        desk.Windows.Add(new FakeWindow { AppId = "other", Executable = @"C:\other.exe", Title = "Other" });
        Assert.Equal(2, model.Tasks.Count);

        var first = model.Tasks[0];
        desk.Apps.Add(new FakeApp("unused", "Unused"));
        Assert.NotSame(first, model.Tasks[0]);

        preferences.TaskbarAppsOn = "where";
        window.Screens.Clear();
        window.Screens.Add(Screen("other", x: 1920));
        window.Raise(nameof(ITaskWindowView.Screens));
        Assert.DoesNotContain(model.Tasks, task => task.Windows.Contains(window));
    }

    [Fact]
    public void Start_and_Search_raise_an_event_and_open_nothing()
    {
        using var preferences = Open();
        var desk = new Desk(preferences);
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

    private sealed class Desk(Preferences preferences)
    {
        public ObservableCollection<ITaskWindow> Windows { get; } = [];

        public ObservableCollection<FakeApp> Apps { get; } = [];

        public PinList Pins { get; } = new();

        public ActiveSource ActiveSource { get; } = new();

        public ITaskWindow? Active { get; set; }

        public List<string> Started { get; } = [];

        public TaskbarModel Bar(ScreenInfo screen, bool isMain) => new(
            Windows,
            Windows,
            Find,
            Apps,
            () => Pins.Ids,
            Pins,
            () => Active,
            ActiveSource,
            preferences,
            screen,
            isMain,
            Started.Add);

        private ITaskApp? Find(string name) =>
            Apps.FirstOrDefault(app => app.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Apps.FirstOrDefault(app => app.AppId.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Apps.FirstOrDefault(app => app.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PinList : INotifyPropertyChanged
    {
        public IReadOnlyList<string> Ids { get; set; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ids)));
    }

    private sealed class ActiveSource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("ActiveWindow"));
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

    private sealed class FakeWindow : ITaskWindow, ITaskWindowView, ITaskControl, INotifyPropertyChanged
    {
        public string AppId { get; set; } = "";

        public string Executable { get; set; } = "";

        public string Title { get; set; } = "";

        public string Icon { get; set; } = "";

        public bool Activated { get; set; }

        public bool Minimized { get; set; }

        public bool Attention { get; set; }

        public List<ScreenInfo> Screens { get; } = [];

        IReadOnlyList<ScreenInfo> ITaskWindowView.Screens => Screens;

        public int Activations { get; private set; }

        public int Minimizations { get; private set; }

        public void Activate() => Activations++;

        public void Minimize() => Minimizations++;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void SetActivated(bool activated)
        {
            Activated = activated;
            Raise(nameof(ITaskWindow.Activated));
        }
    }
}
