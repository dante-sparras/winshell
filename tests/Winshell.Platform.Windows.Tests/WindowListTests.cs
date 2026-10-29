using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class WindowListTests
{
    private static readonly MonitorFrame Primary = new(0, 0, 1920, 1080, 1);
    private static readonly MonitorFrame Scaled = new(1920, 0, 4480, 1440, 1.5);

    private static TaskbarWindowFacts Shown(bool appWindow = false, bool owned = false, bool tool = false, bool noActivate = false) =>
        new(true, true, false, false, appWindow, owned, tool, noActivate, true, false);

    [Fact]
    public void A_normal_window_with_a_title_is_on_the_taskbar() => Assert.True(WindowRules.ShowsOnTaskbar(Shown()));

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
        bool exists, bool visible, bool shell, bool child, bool appWindow, bool owned, bool tool, bool noActivate, bool title, bool cloaked)
    {
        var facts = new TaskbarWindowFacts(exists, visible, shell, child, appWindow, owned, tool, noActivate, title, cloaked);
        Assert.False(WindowRules.ShowsOnTaskbar(facts));
    }

    [Fact]
    public void An_owned_window_that_asks_for_a_taskbar_button_is_listed() =>
        Assert.True(WindowRules.ShowsOnTaskbar(Shown(appWindow: true, owned: true, tool: true, noActivate: true)));

    [Fact]
    public void App_identity_prefers_the_window_id_and_otherwise_uses_the_program_name()
    {
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", WindowRules.AppId("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"C:\Windows\System32\ApplicationFrameHost.exe"));
        Assert.Equal("chrome", WindowRules.AppId("", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal("Chrome", WindowRules.AppId("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal("", WindowRules.AppId("", ""));
    }

    [Fact]
    public void A_packaged_app_icon_comes_from_Start_and_every_other_icon_from_the_program()
    {
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", WindowRules.IconPath("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"C:\Windows\System32\ApplicationFrameHost.exe"));
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", WindowRules.IconPath("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal(@"C:\Program Files\App\app.exe", WindowRules.IconPath("", @"C:\Program Files\App\app.exe"));
    }

    [Fact]
    public void Moving_to_a_scaled_monitor_keeps_the_logical_size_and_offset()
    {
        var window = new RECT(100, 80, 500, 380);
        var moved = WindowRules.MoveOnto(window, Primary, Scaled);
        Assert.Equal(new RECT(2070, 120, 2670, 570), moved);
        Assert.Equal(window, WindowRules.MoveOnto(moved, Scaled, Primary));
    }

    [Fact]
    public void A_window_that_would_hang_off_the_target_is_slid_back()
    {
        var narrow = new MonitorFrame(1920, 0, 2920, 1080, 1);
        Assert.Equal(new RECT(2520, 0, 2920, 100), WindowRules.MoveOnto(new RECT(1800, 0, 2200, 100), Primary, narrow));
    }

    [Fact]
    public void A_window_larger_than_the_target_shrinks_to_fit() =>
        Assert.Equal(new RECT(0, 0, 800, 600), WindowRules.MoveOnto(new RECT(0, 0, 3000, 2000), new MonitorFrame(0, 0, 4000, 3000, 1), new MonitorFrame(0, 0, 800, 600, 1)));

    [Fact]
    public void A_window_above_the_work_area_starts_at_the_target_corner() =>
        Assert.Equal(new RECT(1920, 0, 2120, 100), WindowRules.MoveOnto(new RECT(-50, -20, 150, 80), Primary, new MonitorFrame(1920, 0, 3840, 1080, 1)));

    [Fact]
    public void A_bad_monitor_frame_leaves_the_window_where_it_is()
    {
        var window = new RECT(10, 10, 20, 20);
        Assert.Equal(window, WindowRules.MoveOnto(window, Primary with { Scale = 0 }, Scaled));
    }

    [Fact]
    public void A_minimized_or_maximized_window_moves_by_its_restored_rectangle()
    {
        var current = new RECT(-32000, -32000, -31800, -31000);
        var restored = new RECT(40, 50, 440, 350);
        Assert.Equal(restored, WindowRules.RectangleToMove(true, false, current, restored));
        Assert.Equal(restored, WindowRules.RectangleToMove(false, true, new RECT(0, 0, 1920, 1080), restored));
        Assert.Equal(current, WindowRules.RectangleToMove(false, false, current, restored));
        Assert.Equal(current, WindowRules.RectangleToMove(true, false, current, default));
    }

    [Fact]
    public void Foreground_and_top_level_events_mark_a_window_and_a_child_event_does_not()
    {
        Assert.True(WindowRules.MarksWindow(NativeMethods.EVENT_SYSTEM_FOREGROUND, 1, 0, 0, false, false));
        Assert.False(WindowRules.MarksWindow(NativeMethods.EVENT_SYSTEM_FOREGROUND, 0, 0, 0, false, false));
        Assert.True(WindowRules.MarksForeground(NativeMethods.EVENT_SYSTEM_FOREGROUND));
        Assert.False(WindowRules.MarksWindow(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, 1, NativeMethods.OBJID_WINDOW, 1, true, true));
        Assert.False(WindowRules.MarksWindow(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, 1, NativeMethods.OBJID_WINDOW, NativeMethods.CHILDID_SELF, true, false));
        Assert.True(WindowRules.MarksWindow(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, 1, NativeMethods.OBJID_WINDOW, NativeMethods.CHILDID_SELF, false, true));
        Assert.False(WindowRules.MarksWindow(NativeMethods.EVENT_OBJECT_DESTROY, 1, NativeMethods.OBJID_WINDOW, NativeMethods.CHILDID_SELF, false, false));
        Assert.True(WindowRules.MarksWindow(NativeMethods.EVENT_OBJECT_DESTROY, 1, NativeMethods.OBJID_WINDOW, NativeMethods.CHILDID_SELF, true, false));
        Assert.True(WindowRules.MarksForeground(NativeMethods.EVENT_SYSTEM_MINIMIZESTART));
        Assert.True(WindowRules.MarksForeground(NativeMethods.EVENT_OBJECT_HIDE));
        Assert.False(WindowRules.MarksForeground(NativeMethods.EVENT_OBJECT_NAMECHANGE));
    }

    [Fact]
    public void Only_HSHELL_FLASH_asks_for_attention()
    {
        Assert.True(WindowRules.IsFlash(0x8006));
        Assert.False(WindowRules.IsFlash(6));
        Assert.False(WindowRules.IsFlash(0x8004));
        Assert.False(WindowRules.IsFlash(1));
    }

    [Fact]
    public void A_window_wants_attention_until_it_is_activated_or_leaves_the_list()
    {
        HashSet<nint> wanting = [1, 2, 3];
        var listed = new HashSet<nint> { 1, 2 };
        Assert.True(WindowRules.SettleAttention(wanting, 1, listed.Contains));
        Assert.Equal([2], wanting);
        Assert.False(WindowRules.SettleAttention(wanting, 0, listed.Contains));
        Assert.Equal([2], wanting);
        Assert.True(WindowRules.SettleAttention(wanting, 2, listed.Contains));
        Assert.Empty(wanting);
    }

    [Fact]
    public void The_window_list_speaks_Windows_names()
    {
        var list = MetaType.Of(typeof(WindowListService));
        Assert.True(list.Properties.ContainsKey("windows"));
        Assert.True(list.Properties.ContainsKey("activeWindow"));
        Assert.False(list.Properties.ContainsKey("toplevels"));
        Assert.False(list.Properties.ContainsKey("activeToplevel"));

        var window = MetaType.Of(typeof(AppWindow));
        Assert.True(window.Properties.ContainsKey("appId"));
        Assert.True(window.Properties.ContainsKey("executable"));
        Assert.True(window.Properties.ContainsKey("icon"));
        Assert.False(window.Properties["fullscreen"].CanWrite);
        Assert.False(window.Properties["attention"].CanWrite);
        Assert.True(window.Properties["maximized"].CanWrite);
        Assert.True(window.Properties["minimized"].CanWrite);
        Assert.True(window.Methods.ContainsKey("activate"));
        Assert.True(window.Methods.ContainsKey("close"));
        Assert.True(window.Methods.ContainsKey("moveTo"));
        Assert.True(window.Properties.ContainsKey("desktopId"));
        Assert.True(window.Properties.ContainsKey("desktop"));
        Assert.True(window.Methods.ContainsKey("moveToDesktop"));
        Assert.False(window.Methods.ContainsKey("fullscreenOn"));
        Assert.False(window.Methods.ContainsKey("setRectangle"));
        Assert.False(window.Methods.ContainsKey("unsetRectangle"));
    }
}
