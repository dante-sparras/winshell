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
}
