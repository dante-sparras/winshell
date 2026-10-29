using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary><c>WINSHELL_LOCAL_DIR</c> is the local root. A blank value keeps the Windows profile folder.</summary>
public class LocalDirOverrideTests
{
    [Fact]
    public void A_local_dir_override_is_a_fully_qualified_root()
    {
        var dir = Path.Combine("winshell-local", "override");

        var root = ShellLocations.LocalRootFrom(dir);

        Assert.Equal(Path.GetFullPath(dir), root);
        Assert.True(Path.IsPathFullyQualified(root));
    }

    [Fact]
    public void A_blank_local_dir_override_uses_the_windows_profile()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winshell");

        Assert.Equal(local, ShellLocations.LocalRootFrom(null));
        Assert.Equal(local, ShellLocations.LocalRootFrom("  "));
    }
}
