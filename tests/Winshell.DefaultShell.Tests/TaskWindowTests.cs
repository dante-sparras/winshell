using Winshell.DefaultShell.Taskbar;
using Winshell.Windows;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskWindowTests
{
    [Fact]
    public void A_fresh_adapter_for_the_same_window_compares_equal()
    {
        var source = new Source();
        var first = Window(source, 1, "One");
        var second = Window(source, 2, "Two");
        IReadOnlyList<ITaskWindow> windows = [new TaskWindow(first), new TaskWindow(second)];

        Assert.Equal(new TaskWindow(first), new TaskWindow(first));
        Assert.NotEqual(new TaskWindow(first), new TaskWindow(second));
        Assert.Contains(new TaskWindow(second), windows);

        var task = Tasks.Build(windows, [], _ => null, _ => true, true).Single();
        var action = Assert.IsType<TaskClick.Activate>(Tasks.Click(task, new TaskWindow(second)));
        Assert.Equal(new TaskWindow(second), action.Window);
    }

    [Fact]
    public void Activate_minimize_and_a_change_reach_the_window()
    {
        var source = new Source();
        var window = Window(source, 7, "Notepad");
        var adapter = new TaskWindow(window);
        string? changed = null;
        adapter.PropertyChanged += (_, e) => changed = e.PropertyName;

        adapter.Activate();
        adapter.Minimize();
        window.SetActivated(true);

        Assert.Equal(7, source.Activated);
        Assert.Equal((7, true), (source.MinimizedHwnd, source.Minimized));
        Assert.Equal(nameof(AppWindow.Activated), changed);
        Assert.True(adapter.Activated);
    }

    private static AppWindow Window(Source source, nint hwnd, string title) =>
        new(new WindowInfo(hwnd, title, "notes", @"C:\notes.exe", @"C:\notes.exe", 0, false, false, false, ""), source, null);

    private sealed class Source : IWindowSource
    {
        public nint Activated { get; private set; }

        public nint MinimizedHwnd { get; private set; }

        public bool Minimized { get; private set; }

        public IReadOnlyDictionary<nint, WindowInfo> Windows { get; } = new Dictionary<nint, WindowInfo>();

        public nint Active => 0;

        public event Action? Changed { add { } remove { } }

        public void Start(IEventLoop loop, Log log)
        {
        }

        public bool WantsAttention(nint hwnd) => false;

        public ScreenInfo? Screen(string deviceName) => null;

        public string DesktopId(nint hwnd) => "";

        public bool MoveToDesktop(nint hwnd, Guid desktop) => false;

        public void Activate(nint hwnd) => Activated = hwnd;

        public void Close(nint hwnd)
        {
        }

        public void SetMaximized(nint hwnd, bool maximized)
        {
        }

        public void SetMinimized(nint hwnd, bool minimized)
        {
            MinimizedHwnd = hwnd;
            Minimized = minimized;
        }

        public void MoveToMonitor(nint hwnd, string deviceName)
        {
        }
    }
}
