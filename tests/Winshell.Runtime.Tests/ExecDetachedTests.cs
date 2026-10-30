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
}
