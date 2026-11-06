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

    [Fact]
    public void A_preview_leaves_out_the_invisible_resize_borders()
    {
        // A 1280x720 frame with 7-pixel borders on the left, right and bottom, as Windows 11 draws a normal window.
        Assert.Equal(new RECT(7, 0, 1287, 720),
            WindowRules.PreviewSource(new RECT(93, 100, 1387, 827), new RECT(100, 100, 1380, 820)));
        // A maximized window hangs over the monitor by its borders; the frame is the monitor.
        Assert.Equal(new RECT(8, 8, 1928, 1048),
            WindowRules.PreviewSource(new RECT(-8, -8, 1928, 1048), new RECT(0, 0, 1920, 1040)));
    }

    [Fact]
    public void Without_a_frame_inside_the_window_the_preview_is_the_whole_window()
    {
        Assert.Equal(new RECT(0, 0, 400, 300), WindowRules.PreviewSource(new RECT(10, 10, 410, 310), default));
        Assert.Equal(new RECT(0, 0, 400, 300),
            WindowRules.PreviewSource(new RECT(10, 10, 410, 310), new RECT(500, 500, 600, 600)));
        Assert.Equal(new RECT(0, 0, 0, 0), WindowRules.PreviewSource(default, new RECT(0, 0, 10, 10)));
    }

    [Fact]
    public void A_preview_fits_its_box_keeping_its_shape_centered()
    {
        Assert.Equal(new RECT(10, 38, 210, 150), WindowRules.FitPreview(new RECT(10, 20, 210, 168), 1280, 720));
        Assert.Equal(new RECT(70, 20, 150, 168), WindowRules.FitPreview(new RECT(10, 20, 210, 168), 540, 1000));
    }

    [Fact]
    public void A_small_window_is_previewed_at_its_own_size_not_enlarged()
    {
        Assert.Equal(new RECT(60, 59, 160, 129), WindowRules.FitPreview(new RECT(10, 20, 210, 168), 100, 70));
    }

    [Fact]
    public void A_preview_with_no_box_or_no_source_is_empty()
    {
        Assert.Equal(default, WindowRules.FitPreview(default, 100, 100));
        Assert.Equal(default, WindowRules.FitPreview(new RECT(0, 0, 100, 100), 0, 100));
    }
}
