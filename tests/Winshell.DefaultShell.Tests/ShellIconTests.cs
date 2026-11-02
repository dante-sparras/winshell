using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class ShellIconTests
{
    [Theory]
    [InlineData(@"C:\Windows\notepad.exe", @"C:\Windows\notepad.exe")]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("", "")]
    [InlineData(@"C:\a!b.exe", @"C:\a!b.exe")]
    public void A_program_path_is_kept_and_a_packaged_app_id_becomes_its_Start_icon(string icon, string path) =>
        Assert.Equal(path, ShellIcon.ShellPath(icon));

    [Fact]
    public void A_path_is_unescaped_stripped_of_file_and_expanded_and_the_query_is_cut()
    {
        var windows = Environment.GetEnvironmentVariable("WINDIR");
        Assert.False(string.IsNullOrEmpty(windows));

        Assert.Equal(windows + @"\notepad.exe", ShellIcon.ShellPath(@"%WINDIR%\notepad.exe"));
        Assert.Equal(windows + @"\notepad.exe", ShellIcon.ShellPath(@"%WINDIR%\notepad.exe?overlay"));
        Assert.Equal(@"C:\Program Files\app.exe", ShellIcon.ShellPath(@"C:\Program%20Files\app.exe"));
        Assert.Equal(@"C:/Windows/notepad.exe", ShellIcon.ShellPath("file:///C:/Windows/notepad.exe"));
        Assert.Equal(@"C:/Windows/notepad.exe", ShellIcon.ShellPath("FILE:///C:/Windows/notepad.exe"));
        Assert.Equal(@"C:\Windows\notepad.exe", ShellIcon.ShellPath(@"file://C:\Windows\notepad.exe"));
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ShellIcon.ShellPath("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App?fallback=none"));
    }
}
