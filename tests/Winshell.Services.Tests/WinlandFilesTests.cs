using Winshell.Winland;

namespace Winshell.Services.Tests;

/// <summary>Winland's config and log, found where Winland itself puts them.</summary>
public sealed class WinlandFilesTests
{
    [Fact]
    public void The_config_is_winland_conf_in_dot_winland_under_the_profile()
    {
        Assert.Equal(@"C:\Users\ada\.winland\winland.conf", WinlandFiles.Config(@"C:\Users\ada"));
    }

    [Theory]
    [InlineData(null, @"C:\Users\ada\.winland\winland.log")]
    [InlineData("", @"C:\Users\ada\.winland\winland.log")]
    [InlineData(" ", @"C:\Users\ada\.winland\winland.log")]
    [InlineData(@"D:\dev\winland-dev-1.json", @"D:\dev\winland.log")]
    public void The_log_is_beside_the_state_file(string? stateFile, string log)
    {
        Assert.Equal(log, WinlandFiles.Log(@"C:\Users\ada", stateFile));
    }
}
