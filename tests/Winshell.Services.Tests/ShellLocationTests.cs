namespace Winshell.Services.Tests;

/// <summary>
/// The config and local roots follow the Windows profile unless <c>WINSHELL_CONFIG_DIR</c> or
/// <c>WINSHELL_LOCAL_DIR</c> names another folder. The folders under the local root are files on disk users find.
/// </summary>
public class ShellLocationTests
{
    [Theory]
    [InlineData(Environment.SpecialFolder.ApplicationData)]
    [InlineData(Environment.SpecialFolder.LocalApplicationData)]
    public void A_blank_override_uses_the_windows_profile(Environment.SpecialFolder profile)
    {
        var expected = Path.Combine(Environment.GetFolderPath(profile), "winshell");

        Assert.Equal(expected, ShellLocations.RootFrom(null, profile));
        Assert.Equal(expected, ShellLocations.RootFrom("  ", profile));
    }

    [Fact]
    public void The_local_override_names_the_local_root()
    {
        // LocalFolder gives every test here its own WINSHELL_LOCAL_DIR.
        var local = Environment.GetEnvironmentVariable("WINSHELL_LOCAL_DIR")!;

        Assert.Equal(local, ShellLocations.Current.LocalRoot);
        Assert.NotEqual(local, ShellLocations.Current.ConfigRoot);
    }

    [Fact]
    public void An_override_is_a_fully_qualified_root()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-override");
        var profile = Environment.SpecialFolder.LocalApplicationData;

        Assert.Equal(dir, ShellLocations.RootFrom(dir, profile));
        Assert.Equal(dir, ShellLocations.RootFrom("  " + dir + "  ", profile));
        Assert.Equal(Path.GetFullPath("winshell-relative"), ShellLocations.RootFrom("winshell-relative", profile));
    }

    [Fact]
    public void The_local_folders_are_under_the_local_root()
    {
        var folders = new ShellLocations(@"C:\config", @"C:\local");

        Assert.Equal(@"C:\local\cache", folders.CacheRoot);
        Assert.Equal(@"C:\local\state", folders.StateRoot);
        Assert.Equal(@"C:\local\data", folders.DataRoot);
        Assert.Equal(@"C:\local\run", folders.RunRoot);
        Assert.Equal(@"C:\local\crashes", folders.CrashRoot);
    }
}
