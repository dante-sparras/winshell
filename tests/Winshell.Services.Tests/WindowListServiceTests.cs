using System.ComponentModel;
using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>The window list over a stand-in, so no real window is read, moved or closed.</summary>
public sealed class WindowListServiceTests
{
    private readonly ManualEventLoop _loop = new();
    private readonly FakeWindows _windows = new();

    private WindowListService Start(VirtualDesktopsService? desktops = null) => new(_loop, Log.Default, desktops, _windows);

    private static WindowInfo Window(nint hwnd, string title = "Title", nint owner = 0, string monitor = "M1") =>
        new(hwnd, title, "app", @"C:\app.exe", "icon", owner, false, false, false, monitor);

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void A_new_window_goes_at_the_end_and_a_window_that_stays_is_the_same_object()
    {
        _windows.Open(Window(1), Window(2));
        using var list = Start();
        var kept = list.Windows[1];

        // The slot window 1 leaves in the tracker's dictionary is what window 3 takes.
        _windows.Items.Remove(1);
        _windows.Open(Window(3));
        _windows.Push();
        _loop.Process();

        Assert.Equal([2, 3], list.Windows.Select(window => (int)window.Hwnd));
        Assert.Same(kept, list.Windows[0]);
    }

    [Fact]
    public void A_change_reports_only_the_properties_that_differ()
    {
        _windows.Open(Window(1, "Before"));
        using var list = Start();
        var window = list.Windows[0];
        var changes = Watch(window);

        _windows.Open(Window(1, "After") with { Maximized = true });
        _windows.Push();
        _loop.Process();

        Assert.Equal([nameof(AppWindow.Title), nameof(AppWindow.Maximized)], changes);
        Assert.Equal("After", window.Title);
        Assert.True(window.Maximized);
    }

    [Fact]
    public void A_closed_window_leaves_the_list_before_it_says_so()
    {
        _windows.Open(Window(1));
        _windows.Active = 1;
        using var list = Start();
        var window = list.Windows[0];
        Assert.Same(window, list.ActiveWindow);
        var stillListed = true;
        window.Closed += (_, _) => stillListed = list.Windows.Contains(window);

        _windows.Items.Remove(1);
        _windows.Active = 0;
        _windows.Push();
        _loop.Process();

        Assert.False(stillListed);
        Assert.Null(list.ActiveWindow);
    }

    [Fact]
    public void The_window_in_front_is_activated_and_a_flash_shows_until_it_stops()
    {
        _windows.Open(Window(1), Window(2));
        _windows.Active = 1;
        _windows.Wanting.Add(2);
        using var list = Start();

        Assert.True(list.Windows[0].Activated);
        Assert.False(list.Windows[1].Activated);
        Assert.True(list.Windows[1].Attention);

        _windows.Active = 2;
        _windows.Wanting.Clear();
        _windows.Push();
        _loop.Process();

        Assert.Same(list.Windows[1], list.ActiveWindow);
        Assert.False(list.Windows[0].Activated);
        Assert.True(list.Windows[1].Activated);
        Assert.False(list.Windows[1].Attention);
    }

    [Fact]
    public void A_parent_is_the_owner_when_the_owner_is_listed()
    {
        _windows.Open(Window(2, owner: 1), Window(3, owner: 9), Window(1));
        using var list = Start();

        Assert.Same(list.Windows.Single(window => window.Hwnd == 1), list.Windows[0].Parent);
        Assert.Null(list.Windows[1].Parent);
    }

    [Fact]
    public void Setting_a_state_asks_Windows_and_the_value_follows_what_Windows_reports()
    {
        _windows.Open(Window(1));
        using var list = Start();
        var window = list.Windows[0];

        window.Maximized = true;
        window.Minimized = true;

        Assert.Equal(["maximize:1:True", "minimize:1:True"], _windows.Calls);
        Assert.False(window.Maximized);
        Assert.False(window.Minimized);
    }

    [Fact]
    public void Requests_go_to_that_window()
    {
        _windows.Open(Window(7));
        using var list = Start();
        var window = list.Windows[0];
        var screen = new ScreenInfo("M2", "", "", "", "", default, default, 1, 0, 0, 60, false);

        window.Activate();
        window.Close();
        window.MoveTo(screen);

        Assert.Equal(["activate:7", "close:7", "move:7:M2"], _windows.Calls);
    }

    [Fact]
    public void A_window_is_on_the_screen_Windows_says_is_nearest()
    {
        var screen = new ScreenInfo("M1", "", "", "", "", default, default, 1, 0, 0, 60, true);
        _windows.Screens["M1"] = screen;
        _windows.Open(Window(1), Window(2, monitor: "gone"));
        using var list = Start();
        var changes = Watch(list.Windows[1]);

        Assert.Same(screen, Assert.Single(list.Windows[0].Screens));
        Assert.Empty(list.Windows[1].Screens);

        _windows.Open(Window(2, monitor: "M1"));
        _windows.Push();
        _loop.Process();

        Assert.Equal([nameof(AppWindow.Screens)], changes);
        Assert.Same(screen, Assert.Single(list.Windows[1].Screens));
    }

    [Fact]
    public void A_window_finds_its_desktop_and_moving_it_reports_the_new_one()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var desktops = new VirtualDesktopsService(_loop, Log.Default, FakeDesktops.Holding(first, first, second));
        _windows.Open(Window(1));
        _windows.DesktopIds[1] = first.ToString("D");
        using var list = Start(desktops);
        var window = list.Windows[0];
        var changes = Watch(window);
        Assert.Same(desktops.Desktops[0], window.Desktop);

        window.MoveToDesktop(desktops.Desktops[1]);

        Assert.Equal([nameof(AppWindow.DesktopId), nameof(AppWindow.Desktop)], changes);
        Assert.Same(desktops.Desktops[1], window.Desktop);
    }

    [Fact]
    public void A_window_says_its_desktop_may_have_changed_after_each_read_of_the_list()
    {
        var only = Guid.NewGuid();
        var source = FakeDesktops.Holding(only, only);
        using var desktops = new VirtualDesktopsService(_loop, Log.Default, source);
        _windows.Open(Window(1));
        using var list = Start(desktops);
        var changes = Watch(list.Windows[0]);

        desktops.Create();

        Assert.Equal([nameof(AppWindow.DesktopId), nameof(AppWindow.Desktop)], changes);
    }

    [Fact]
    public void Without_a_desktop_list_a_window_still_has_a_desktop_id()
    {
        _windows.Open(Window(1));
        _windows.DesktopIds[1] = "id";
        using var list = Start();

        Assert.Equal("id", list.Windows[0].DesktopId);
        Assert.Null(list.Windows[0].Desktop);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_windows()
    {
        var list = Start();

        _windows.Open(Window(1));
        _windows.Push();
        list.Dispose();
        _loop.Process();

        Assert.Empty(list.Windows);
        Assert.False(_windows.HasListeners);
    }

    private sealed class FakeWindows : IWindowSource
    {
        public Dictionary<nint, WindowInfo> Items { get; } = [];

        public HashSet<nint> Wanting { get; } = [];

        public Dictionary<string, ScreenInfo> Screens { get; } = [];

        public Dictionary<nint, string> DesktopIds { get; } = [];

        public List<string> Calls { get; } = [];

        public IReadOnlyDictionary<nint, WindowInfo> Windows => Items;

        public nint Active { get; set; }

        public bool HasListeners => Changed is not null;

        public event Action? Changed;

        public void Open(params WindowInfo[] windows)
        {
            foreach (var window in windows)
                Items[window.Hwnd] = window;
        }

        public void Push() => Changed?.Invoke();

        public void Start(IEventLoop loop, Log log)
        {
        }

        public bool WantsAttention(nint hwnd) => Wanting.Contains(hwnd);

        public ScreenInfo? Screen(string deviceName) => Screens.GetValueOrDefault(deviceName);

        public string DesktopId(nint hwnd) => DesktopIds.GetValueOrDefault(hwnd, "");

        public bool MoveToDesktop(nint hwnd, Guid desktop)
        {
            DesktopIds[hwnd] = desktop.ToString("D");
            return true;
        }

        public void Activate(nint hwnd) => Calls.Add($"activate:{hwnd}");

        public void Close(nint hwnd) => Calls.Add($"close:{hwnd}");

        public void SetMaximized(nint hwnd, bool maximized) => Calls.Add($"maximize:{hwnd}:{maximized}");

        public void SetMinimized(nint hwnd, bool minimized) => Calls.Add($"minimize:{hwnd}:{minimized}");

        public void MoveToMonitor(nint hwnd, string deviceName) => Calls.Add($"move:{hwnd}:{deviceName}");
    }
}
