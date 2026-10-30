using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Shell pragmas resolve from the shell file, expand environment variables, and stay put across a reload.</summary>
public class PragmaPathTests
{
    [Fact]
    public void A_relative_data_dir_is_next_to_the_shell_file()
    {
        using var files = new TestShell(("shell.qml", """
            //@ pragma DataDir stash
            import Winshell
            ShellRoot {}
            """));
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        try
        {
            Assert.Equal(Path.GetFullPath(Path.Combine(files.Directory, "stash")), host.DataDir);
            Assert.NotEqual(Path.GetFullPath("stash"), host.DataDir);
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void A_data_dir_expands_environment_variables()
    {
        var folder = Path.Combine(Path.GetTempPath(), "winshell-pragma-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WINSHELL_PRAGMA_TEST_DIR", folder);
        try
        {
            using var files = new TestShell(("shell.qml", """
                //@ pragma CacheDir %WINSHELL_PRAGMA_TEST_DIR%\kept
                import Winshell
                ShellRoot {}
                """));
            var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
            try
            {
                Assert.Equal(Path.GetFullPath(Path.Combine(folder, "kept")), host.CacheDir);
            }
            finally
            {
                host.Dispose();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINSHELL_PRAGMA_TEST_DIR", null);
        }
    }

    [Fact]
    public void Reload_keeps_the_data_dir_from_launch_and_warns()
    {
        using var files = new TestShell(("shell.qml", """
            //@ pragma DataDir stash
            import Winshell
            ShellRoot {}
            """));
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        try
        {
            Assert.True(host.Start(), string.Join("\n", files.Messages));
            var data = host.DataDir;
            var shellId = host.ShellId;
            files.Write("shell.qml", """
                //@ pragma DataDir other
                //@ pragma ShellId changed
                import Winshell
                ShellRoot {}
                """);
            host.Reload(true);
            files.Loop.Process();
            Assert.Equal(data, host.DataDir);
            Assert.Equal(shellId, host.ShellId);
            Assert.Contains(files.Messages, message => message.StartsWith("Warn scene: Pragmas changed.", StringComparison.Ordinal));
        }
        finally
        {
            host.Dispose();
        }
    }
}
