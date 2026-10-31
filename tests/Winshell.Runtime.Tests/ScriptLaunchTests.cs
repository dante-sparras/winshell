using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A script starts the way Windows would start it. The plans are read, and nothing runs.</summary>
public sealed class ScriptLaunchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public ScriptLaunchTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Script(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "");
        return path;
    }

    private static ProcessSpec Spec(string path, Dictionary<string, string?>? environment = null) =>
        new([path], environment ?? [], false, null);

    [Fact]
    public void A_powershell_script_keeps_the_users_execution_policy()
    {
        var plan = ProcessLauncher.PlanDetached(Spec(Script("run.ps1")));

        Assert.DoesNotContain("Bypass", plan.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExecutionPolicy", plan.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-NoProfile -File", plan.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_script_opens_with_what_windows_has_for_it()
    {
        var path = Script("run.sh");

        var plan = ProcessLauncher.PlanDetached(Spec(path));

        Assert.True(plan.UseShellExecute);
        Assert.Equal(path, plan.File);
    }

    [Fact]
    public void A_shell_script_with_an_environment_still_needs_an_interpreter()
    {
        var path = Script("run.sh");

        var plan = ProcessLauncher.PlanDetached(Spec(path, new Dictionary<string, string?> { ["A"] = "1" }));

        Assert.False(plan.UseShellExecute);
        Assert.NotEqual(path, plan.File);
    }
}
