namespace Winshell.Services.Tests;

public class ShellLocationTests
{
    [Fact]
    public void Folders_follow_the_windows_user_profile()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winshell");
        Assert.Equal(Path.Combine(roaming, "winshell"), ShellLocations.ConfigRoot);
        Assert.Equal(local, ShellLocations.LocalRootFrom(null));
        Assert.Equal(local, ShellLocations.LocalRootFrom("  "));
        Assert.Equal(Path.Combine(ShellLocations.LocalRoot, "cache"), ShellLocations.CacheRoot);
        Assert.Equal(Path.Combine(ShellLocations.LocalRoot, "state"), ShellLocations.StateRoot);
        Assert.Equal(Path.Combine(ShellLocations.LocalRoot, "data"), ShellLocations.DataRoot);
        Assert.Equal(Path.Combine(ShellLocations.LocalRoot, "run"), ShellLocations.RunRoot);
    }
}
