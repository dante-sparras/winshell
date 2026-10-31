
namespace Winshell.Platform.Windows.Tests;

/// <summary>A crash notice stays as long as the person asked Windows to keep notifications up, and never less.</summary>
public class CrashNoticeTimeTests
{
    [Theory]
    [InlineData(5u, 25)]
    [InlineData(25u, 25)]
    [InlineData(60u, 60)]
    [InlineData(300u, 300)]
    public void The_notice_follows_a_longer_setting_only(uint setting, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ShellNotices.CrashTimeoutFor(setting));
}
