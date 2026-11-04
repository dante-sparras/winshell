using System.Runtime.InteropServices;
using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// The real <c>winshell dev</c> lets its shell quit, as <c>winshell kill</c> asks it to, before it ends or starts it again.
/// A class apart from <see cref="DevCommandTests"/>, so their sessions run at the same time.
/// </summary>
[Trait("Category", "Slow")]
public sealed class DevShellQuitTests : IDisposable
{
    private readonly DevProject _project = new();

    public void Dispose() => _project.Dispose();

    [Fact(Timeout = 170_000)]
    public void Ctrl_Break_stops_dotnet_watch_and_the_shell()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        Assert.True(GetConsoleWindow() != 0 || GetConsoleProcessList([0], 1) > 0, "The test program has no console to send a Ctrl+Break on.");
        _project.StartDev(ownGroup: true);
        Wait.Until(() => _project.Value() == (0, "one"), "the first build", DevProject.BuildPatience);
        Assert.Contains(_project.FromProject(), p => p.StartsWith("Dev.exe ", StringComparison.Ordinal));
        var id = _project.InstanceId();

        // Ctrl+C reaches every process on the console, this test's too. Ctrl+Break can be sent to one process group.
        // Dev is set: StartDev ran above.
        Assert.True(GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)_project.Dev!.Id), "Ctrl+Break was not sent.");

        Assert.True(_project.Dev.WaitForExit(TimeSpan.FromSeconds(30)), "winshell dev did not exit.");
        Wait.Until(() => _project.FromProject().Count == 0, "dotnet watch and the shell to end", TimeSpan.FromSeconds(30));
        // The shell quit, as winshell kill asks it to, before dotnet watch ended: a shell that is only ended marks no exit.
        var record = File.ReadAllText(Path.Combine(_project.Folder, "local", "run", id, "instance.json"));
        Assert.NotNull(JsonSerializer.Deserialize(record, InstanceJson.Default.InstanceInfo)?.ExitedAt);
    }

    // Two builds: the first start, and the one the XAML needs.
    [Fact(Timeout = 300_000)]
    public void Xaml_that_needs_a_build_lets_the_shell_quit_before_it_starts_again()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        _project.StartDev();
        Wait.Until(() => _project.Value() == (0, "one"), "the first build", DevProject.BuildPatience);
        var shell = _project.ShellProcess();
        var id = _project.InstanceId();

        _project.Label("two", needsBuild: true);
        Wait.Until(() => _project.Printed("Building, because the XAML you saved needs it."), "the build the XAML needs");
        Wait.Until(() => _project.Shown() == "two", "the shell to start again", DevProject.BuildPatience);

        Assert.NotEqual(shell, _project.ShellProcess());
        // The shell quit, as winshell kill asks it to: a shell that is only ended marks no exit.
        var record = File.ReadAllText(Path.Combine(_project.Folder, "local", "run", id, "instance.json"));
        Assert.NotNull(JsonSerializer.Deserialize(record, InstanceJson.Default.InstanceInfo)?.ExitedAt);
    }

    private const uint CtrlBreakEvent = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processes, uint count);
}
