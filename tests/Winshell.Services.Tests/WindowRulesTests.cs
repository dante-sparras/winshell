using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;
using Winshell.Windows;

namespace Winshell.Services.Tests;

public sealed class WindowRulesTests
{
    [Fact]
    public void App_identity_prefers_the_window_id_and_otherwise_uses_the_program_name()
    {
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            WindowRules.AppId("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
                @"C:\Windows\System32\ApplicationFrameHost.exe"));
        Assert.Equal("chrome", WindowRules.AppId("", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal("Chrome", WindowRules.AppId("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal("", WindowRules.AppId("", ""));
    }

    [Fact]
    public void A_packaged_app_icon_comes_from_Start_and_every_other_icon_from_the_program()
    {
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            WindowRules.IconPath("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
                @"C:\Windows\System32\ApplicationFrameHost.exe"));
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe",
            WindowRules.IconPath("Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal(@"C:\Program Files\App\app.exe", WindowRules.IconPath("", @"C:\Program Files\App\app.exe"));
    }

    [Theory]
    [InlineData((uint)WINDOW_STYLE.WS_POPUP, 0, 0, 1920, 1080, true)]
    [InlineData((uint)(WINDOW_STYLE.WS_POPUP | WINDOW_STYLE.WS_THICKFRAME), -8, -8, 1928, 1088, true)]
    [InlineData((uint)(WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME), -8, -8, 1928, 1088, false)]
    [InlineData((uint)WINDOW_STYLE.WS_POPUP, 0, 0, 1920, 1079, false)]
    [InlineData((uint)WINDOW_STYLE.WS_POPUP, 1, 0, 1920, 1080, false)]
    public void A_window_is_fullscreen_when_it_has_no_title_bar_and_covers_the_whole_monitor(
        uint style, int left, int top, int right, int bottom, bool fullscreen)
    {
        Assert.Equal(fullscreen,
            WindowRules.CoversMonitor(style, new RECT(left, top, right, bottom), new RECT(0, 0, 1920, 1080)));
    }

    [Fact]
    public void Foreground_and_top_level_events_mark_a_window_and_a_child_event_does_not()
    {
        Assert.True(WindowRules.MarksWindow(Win32.EVENT_SYSTEM_FOREGROUND, 1, 0, 0, false, false));
        Assert.False(WindowRules.MarksWindow(Win32.EVENT_SYSTEM_FOREGROUND, 0, 0, 0, false, false));
        Assert.True(WindowRules.MarksForeground(Win32.EVENT_SYSTEM_FOREGROUND));
        Assert.False(WindowRules.MarksWindow(Win32.EVENT_OBJECT_LOCATIONCHANGE, 1, Win32.OBJID_WINDOW, 1, true, true));
        Assert.False(WindowRules.MarksWindow(Win32.EVENT_OBJECT_LOCATIONCHANGE, 1, Win32.OBJID_WINDOW,
            Win32.CHILDID_SELF, true, false));
        Assert.True(WindowRules.MarksWindow(Win32.EVENT_OBJECT_LOCATIONCHANGE, 1, Win32.OBJID_WINDOW,
            Win32.CHILDID_SELF, false, true));
        Assert.False(WindowRules.MarksWindow(Win32.EVENT_OBJECT_DESTROY, 1, Win32.OBJID_WINDOW, Win32.CHILDID_SELF,
            false, false));
        Assert.True(WindowRules.MarksWindow(Win32.EVENT_OBJECT_DESTROY, 1, Win32.OBJID_WINDOW, Win32.CHILDID_SELF, true,
            false));
        Assert.True(WindowRules.MarksForeground(Win32.EVENT_SYSTEM_MINIMIZESTART));
        Assert.True(WindowRules.MarksForeground(Win32.EVENT_OBJECT_HIDE));
        Assert.False(WindowRules.MarksForeground(Win32.EVENT_OBJECT_NAMECHANGE));
    }

    [Fact]
    public void Caret_cursor_and_child_objects_are_not_window_events()
    {
        Assert.True(WindowRules.IsWindowObject(Win32.OBJID_WINDOW, Win32.CHILDID_SELF));
        Assert.False(WindowRules.IsWindowObject(-8 /* OBJID_CARET */, Win32.CHILDID_SELF));
        Assert.False(WindowRules.IsWindowObject(-9 /* OBJID_CURSOR */, Win32.CHILDID_SELF));
        Assert.False(WindowRules.IsWindowObject(Win32.OBJID_WINDOW, 1));
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
        WindowRules.SettleAttention(wanting, 1, listed.Contains);
        Assert.Equal([2], wanting);
        WindowRules.SettleAttention(wanting, 0, listed.Contains);
        Assert.Equal([2], wanting);
        WindowRules.SettleAttention(wanting, 2, listed.Contains);
        Assert.Empty(wanting);
    }
}
