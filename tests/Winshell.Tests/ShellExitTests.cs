using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Winshell.Tests;

/// <summary>
/// Ending a shell under <c>winshell run</c>, with what its supervisor reads when it exits. The test is the shell and its
/// supervisor: it serves the control pipe here for a process of its own, which it ends when the shell would exit.
/// </summary>
public sealed class ShellExitTests : IDisposable
{
    private readonly string _pipe = "winshell-test-" + Guid.NewGuid().ToString("N");

    // cmd waits on its open input. Its banner stays out of this process's output, which carries xUnit's results.
    private readonly Process _process = Process.Start(
        new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        })!;

    private readonly ShellExit.Supervision _supervision;

    /// <summary>Waits as long as the test does, so only the shell's exit ends a quit.</summary>
    private static readonly ShellExit.Patience Patient = new(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    public ShellExitTests()
    {
        _supervision = new ShellExit.Supervision(_process.Id);
    }

    private InstanceInfo Instance => new(
        "abc123", _process.Id, "shell", @"C:\cfg\MyShell.exe", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        _pipe, "log.txt",
        _process.StartTime.ToUniversalTime().Ticks, ProcessName: _process.ProcessName);

    /// <summary>A shell posts its quit before it answers, so it can exit before its answer is written.</summary>
    [Fact]
    public async Task A_shell_that_exits_before_it_answers_keeps_its_reload()
    {
        await using var pipe = IpcServer.OpenFirst(_pipe);
        var asked = ReadRequest(pipe);
        var quit = ShellExit.QuitAsync(Instance, ExitIntent.Reload, Patient, ShellExit.Ending.Never);
        await asked;

        await ExitAsync(pipe);

        Assert.True((await quit).Exited);
        Assert.Equal(ExitIntent.Reload, _supervision.Intent);
    }

    /// <summary><c>winshell kill</c> and <c>winshell reload</c> both asked before the shell quit.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stop_outranks_a_reload_in_either_order(bool stopFirst)
    {
        var (first, second) = stopFirst ? (ExitIntent.Stop, ExitIntent.Reload) : (ExitIntent.Reload, ExitIntent.Stop);
        await using var firstPipe = IpcServer.OpenFirst(_pipe);
        var firstAsked = ReadRequest(firstPipe);
        var firstQuit = ShellExit.QuitAsync(Instance, first, Patient, ShellExit.Ending.Never);
        await firstAsked;
        await using var secondPipe = IpcServer.OpenNext(_pipe);
        var secondAsked = ReadRequest(secondPipe);
        var secondQuit = ShellExit.QuitAsync(Instance, second, Patient, ShellExit.Ending.Never);
        await secondAsked;

        await ExitAsync(firstPipe, secondPipe);

        Assert.True((await firstQuit).Exited);
        Assert.True((await secondQuit).Exited);
        Assert.Equal(ExitIntent.Stop, _supervision.Intent);
    }

    /// <summary>Takes one request and leaves it unanswered.</summary>
    private static async Task ReadRequest(NamedPipeServerStream pipe)
    {
        await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The shell exits without answering, as one that quit before its answer was written.</summary>
    private async Task ExitAsync(params NamedPipeServerStream[] pipes)
    {
        _process.Kill();
        await _process.WaitForExitAsync(TestContext.Current.CancellationToken);
        foreach (var pipe in pipes)
            await pipe.DisposeAsync();
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit(5000);
        }

        _supervision.Dispose();
        _process.Dispose();
    }
}
