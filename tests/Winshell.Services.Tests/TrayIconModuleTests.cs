using Winshell.Tray;

namespace Winshell.Services.Tests;

/// <summary>An app picks the module a tray icon's resource is loaded from, so a path that is not local is never opened.</summary>
public sealed class TrayIconModuleTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\shell32.dll")]
    [InlineData(@"\\?\C:\Windows\System32\shell32.dll")]
    [InlineData(@"d:/apps/app.exe")]
    [InlineData("shell32.dll")]
    [InlineData("")]
    public void A_local_path_is_accepted(string path)
    {
        Assert.True(IconImage.IsLocalPath(path));
    }

    [Theory]
    [InlineData(@"\\server\share\x.dll")]
    [InlineData(@"\\?\UNC\server\share\x.dll")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"\\?\GLOBALROOT\Device\x.dll")]
    [InlineData(@"\\?\C:")]
    [InlineData("//server/share/x.dll")]
    [InlineData(@"\/server\share\x.dll")]
    public void A_network_or_device_path_is_refused(string path)
    {
        Assert.False(IconImage.IsLocalPath(path));
    }
}
