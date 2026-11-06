using System.ComponentModel;
using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>The window list over a fake backend, so no real window is read, moved or closed.</summary>
public sealed class WindowListServiceTests
{
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";

    private readonly ManualEventLoop _loop = new();
    private readonly FakeWindowBackend _windows = new();

    private WindowListService Start(VirtualDesktopsService? desktops = null)
    {
        return new WindowListService(_loop, Log.Default, desktops, _windows);
    }

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void A_new_window_goes_at_the_end_and_a_window_that_stays_is_the_same_object()
    {
        _windows.Show(1);
        _windows.Show(2);
        using var list = Start();
        var kept = list.Windows[1];

        _windows.Destroy(1);
        _windows.Show(3);
        _loop.Process();

        Assert.Equal([2, 3], list.Windows.Select(window => (int)window.Hwnd));
        Assert.Same(kept, list.Windows[0]);
    }

    [Theory]
    [InlineData(false, true, false, false, false, false, false, false, true, false)]
    [InlineData(true, false, false, false, false, false, false, false, true, false)]
    [InlineData(true, true, true, false, false, false, false, false, true, false)]
    [InlineData(true, true, false, true, false, false, false, false, true, false)]
    [InlineData(true, true, false, false, false, true, false, false, true, false)]
    [InlineData(true, true, false, false, false, false, true, false, true, false)]
    [InlineData(true, true, false, false, false, false, false, true, true, false)]
    [InlineData(true, true, false, false, false, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false, false, false, true, true)]
    public void Windows_the_taskbar_skips_stay_out_of_the_list(
        bool exists, bool visible, bool shell, bool child, bool appWindow, bool owned, bool tool, bool noActivate,
        bool title, bool cloaked)
    {
        _windows.Show(1, FakeWindowBackend.Window(),
            new TaskbarWindowFacts(exists, visible, shell, child, appWindow, owned, tool, noActivate, title, cloaked));
        _windows.Show(2);

        using var list = Start();

        Assert.Equal([2], list.Windows.Select(window => (int)window.Hwnd));
    }

    [Fact]
    public void An_owned_window_that_asks_for_a_taskbar_button_is_listed()
    {
        _windows.Show(1, FakeWindowBackend.Window(),
            FakeWindowBackend.Normal with { AppWindow = true, Owned = true, ToolWindow = true, NoActivate = true });

        using var list = Start();

        Assert.Equal([1], list.Windows.Select(window => (int)window.Hwnd));
    }

    [Fact]
    public void A_change_reports_only_the_properties_that_differ()
    {
        _windows.Show(1, "Before");
        using var list = Start();
        var window = list.Windows[0];
        var changes = Watch(window);

        _windows.Show(1, FakeWindowBackend.Window("After") with { Maximized = true });
        _loop.Process();

        Assert.Equal([nameof(AppWindow.Title), nameof(AppWindow.Maximized)], changes);
        Assert.Equal("After", window.Title);
        Assert.True(window.Maximized);
    }

    [Fact]
    public void Title_changes_before_the_loop_turns_raise_Title_once_with_the_last_value()
    {
        _windows.Show(1, "First");
        using var list = Start();
        var window = list.Windows[0];
        var changes = Watch(window);

        _windows.Show(1, "Second");
        _windows.Show(1, "Third");
        _windows.Show(1, "Last");
        _loop.Process();

        Assert.Equal([nameof(AppWindow.Title)], changes);
        Assert.Equal("Last", window.Title);
    }

    [Fact]
    public void A_window_opened_and_closed_before_the_loop_turns_never_appears()
    {
        using var list = Start();
        var listChanges = 0;
        list.Windows.CollectionChanged += (_, _) => listChanges++;

        _windows.Show(1);
        _windows.Destroy(1);
        _loop.Process();

        Assert.Equal(0, listChanges);
    }

    [Fact]
    public void A_window_that_changes_while_another_is_read_is_listed_afterwards()
    {
        _windows.Show(1);
        using var list = Start();
        _windows.Reading = hwnd =>
        {
            if (hwnd == 1 && !_windows.Shown.ContainsKey(2))
                _windows.Show(2);
        };

        _windows.Show(1, "Changed");
        _loop.Process();

        Assert.Equal([1, 2], list.Windows.Select(window => (int)window.Hwnd));
    }

    [Fact]
    public void A_closed_window_leaves_the_list_before_it_says_so()
    {
        _windows.Show(1);
        _windows.Foreground = 1;
        using var list = Start();
        var window = list.Windows[0];
        Assert.Same(window, list.ActiveWindow);
        var stillListed = true;
        window.Closed += (_, _) => stillListed = list.Windows.Contains(window);

        _windows.Foreground = 0;
        _windows.Destroy(1);
        _loop.Process();

        Assert.False(stillListed);
        Assert.Null(list.ActiveWindow);
    }

    [Fact]
    public void An_owned_dialog_activates_its_owner_and_its_flash_shows_on_the_owner_until_then()
    {
        _windows.Show(1);
        _windows.Show(3);
        _windows.Owners[2] = 1;
        _windows.Foreground = 3;
        using var list = Start();
        var owner = list.Windows[0];
        var other = list.Windows[1];
        Assert.True(other.Activated);

        _windows.Flash(2);
        _loop.Process();

        Assert.True(owner.Attention);
        Assert.False(owner.Activated);

        _windows.Focus(2);
        _loop.Process();

        Assert.Same(owner, list.ActiveWindow);
        Assert.True(owner.Activated);
        Assert.False(owner.Attention);
        Assert.False(other.Activated);
    }

    [Fact]
    public void A_window_without_an_id_uses_its_package_or_program_until_it_sets_one()
    {
        var chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        _windows.Show(1, FakeWindowBackend.Window() with { PackageAppId = Terminal });
        _windows.Show(2, FakeWindowBackend.Window() with { Executable = chrome });
        using var list = Start();
        var packaged = list.Windows[0];
        var program = list.Windows[1];

        Assert.Equal(Terminal, packaged.AppId);
        Assert.Equal(@"shell:AppsFolder\" + Terminal, packaged.Icon);
        Assert.Equal("chrome", program.AppId);
        Assert.Equal(chrome, program.Icon);

        _windows.Show(1, FakeWindowBackend.Window() with { PackageAppId = "Other_8wekyb3d8bbwe!App" });
        _windows.Show(2, FakeWindowBackend.Window() with { AppUserModelId = Terminal });
        _loop.Process();

        Assert.Equal(Terminal, packaged.AppId);
        Assert.Equal(Terminal, program.AppId);
        Assert.Equal(@"shell:AppsFolder\" + Terminal, program.Icon);
        Assert.Equal(chrome, program.Executable);
    }

    [Fact]
    public void A_parent_is_the_owner_when_the_owner_is_listed()
    {
        _windows.Show(2);
        _windows.Show(3);
        _windows.Show(1);
        _windows.Owners[2] = 1;
        _windows.Owners[3] = 9;
        using var list = Start();

        Assert.Same(list.Windows.Single(window => window.Hwnd == 1), list.Windows[0].Parent);
        Assert.Null(list.Windows[1].Parent);
    }

    [Fact]
    public void Setting_a_state_asks_Windows_and_the_value_follows_what_Windows_reports()
    {
        _windows.Show(1);
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
        _windows.Show(7);
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
        _windows.Show(1);
        _windows.Show(2, FakeWindowBackend.Window() with { Monitor = "gone" });
        using var list = Start();
        var changes = Watch(list.Windows[1]);

        Assert.Same(screen, Assert.Single(list.Windows[0].Screens));
        Assert.Empty(list.Windows[1].Screens);

        _windows.Show(2);
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
        _windows.Show(1);
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
        _windows.Show(1);
        using var list = Start(desktops);
        var changes = Watch(list.Windows[0]);

        desktops.Create();

        Assert.Equal([nameof(AppWindow.DesktopId), nameof(AppWindow.Desktop)], changes);
    }

    [Fact]
    public void Without_a_desktop_list_a_window_still_has_a_desktop_id()
    {
        _windows.Show(1);
        _windows.DesktopIds[1] = "id";
        using var list = Start();

        Assert.Equal("id", list.Windows[0].DesktopId);
        Assert.Null(list.Windows[0].Desktop);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_windows()
    {
        var list = Start();

        _windows.Show(1);
        list.Dispose();
        _loop.Process();

        Assert.Empty(list.Windows);
        Assert.False(_windows.HasListeners);
    }
}
