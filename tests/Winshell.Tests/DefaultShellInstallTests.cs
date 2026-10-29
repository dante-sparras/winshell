namespace Winshell.Tests;

/// <summary>The default shell install drops files a build no longer ships, and leaves a running copy alone.</summary>
public class DefaultShellInstallTests
{
    [Fact]
    public void A_dropped_script_is_removed_and_a_user_file_stays()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "shell.qml"), "WINSHELL_INSTALL_TEST_OLD");
            File.WriteAllText(Path.Combine(dir, "Dropped.qml"), "gone");
            File.WriteAllText(Path.Combine(dir, "launcher.toml"), "pins = []\n");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "Gone.js"), "gone");

            var shell = global::Winshell.DefaultShell.Install(dir, static _ => false);

            Assert.Equal(Path.Combine(dir, "shell.qml"), shell);
            Assert.False(File.Exists(Path.Combine(dir, "Dropped.qml")));
            Assert.False(File.Exists(Path.Combine(dir, "sub", "Gone.js")));
            Assert.Equal("pins = []\n", File.ReadAllText(Path.Combine(dir, "launcher.toml")));
            Assert.Contains("ShellRoot", File.ReadAllText(shell), StringComparison.Ordinal);
            Assert.DoesNotContain("WINSHELL_INSTALL_TEST_OLD", File.ReadAllText(shell), StringComparison.Ordinal);
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void A_running_instance_keeps_the_folder_as_it_is()
    {
        var dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "shell.qml"), "hand edit");
            File.WriteAllText(Path.Combine(dir, "Dropped.qml"), "keep");

            var shell = global::Winshell.DefaultShell.Install(dir, static _ => true);

            Assert.Equal("hand edit", File.ReadAllText(shell));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(dir, "Dropped.qml")));
        }
        finally
        {
            Delete(dir);
        }
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }
}
