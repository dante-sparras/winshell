using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary><c>execDetached</c> plans a ShellExecute start, and does not bypass PowerShell.</summary>
public class ExecDetachedTests
{
    [Fact]
    public void A_normal_start_uses_the_shell_and_the_profile_folder()
    {
        var plan = ProcessLauncher.PlanDetached(new ProcessSpec(["notepad.exe"], new Dictionary<string, string?>(), false, null));

        Assert.True(plan.UseShellExecute);
        Assert.False(plan.NewConsole);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), plan.WorkingDirectory);
        Assert.EndsWith("notepad.exe", plan.File, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_argument_with_a_space_is_quoted_for_the_shell()
    {
        var plan = ProcessLauncher.PlanDetached(new ProcessSpec(["notepad.exe", "my file.txt"], new Dictionary<string, string?>(), false, @"C:\work"));

        Assert.Equal(@"""my file.txt""", plan.Arguments);
        Assert.Equal(@"C:\work", plan.WorkingDirectory);
    }

    [Fact]
    public void A_console_program_with_an_environment_gets_a_new_console()
    {
        var env = new Dictionary<string, string?> { ["WINSHELL_EXEC_TEST"] = "1" };
        var plan = ProcessLauncher.PlanDetached(new ProcessSpec(["cmd.exe"], env, false, null));

        Assert.False(plan.UseShellExecute);
        Assert.True(plan.NewConsole);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), plan.WorkingDirectory);
    }

    [Fact]
    public void A_powershell_script_is_not_run_with_execution_policy_bypass()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-exec-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(path, "Write-Output hi\n");
        try
        {
            var (_, args) = ProcessLauncher.Resolve([path]);
            Assert.Contains("-File", args);
            Assert.Contains(path, args);
            Assert.DoesNotContain("-ExecutionPolicy", args);
            Assert.DoesNotContain("Bypass", args);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_shell_script_is_not_forced_through_bash()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-exec-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(path, "echo hi\n");
        try
        {
            var (file, args) = ProcessLauncher.Resolve([path]);
            Assert.Equal(Path.GetFullPath(path), file);
            Assert.Empty(args);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
