using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Winshell.Commands;

namespace Winshell.Tests;

/// <summary>
/// <c>winshell reload</c> of a shell under <c>winshell run</c>, once the build is done. The test is the shell and its
/// supervisor: it serves the control pipe here for a process of its own, which it ends when the shell would exit.
/// </summary>
public sealed class SupervisedReloadTests : IDisposable
{
    private readonly string _pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
    // cmd waits on its open input. Its banner stays out of this process's output, which carries xUnit's results.
    private readonly Process _process = Process.Start(
        new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true })!;
    private readonly ShellProcess _shell;

    public SupervisedReloadTests() => _shell = new ShellProcess(_process);

    /// <summary>A shell posts its quit before it answers, so it can exit before its answer is written.</summary>
    [Fact]
    public async Task A_shell_that_exits_before_it_answers_keeps_its_reload()
    {
        await using var pipe = IpcServer.OpenFirst(_pipe);
        var exiting = ExitOnRequest(pipe);
        var info = new InstanceInfo(
            "abc123", _process.Id, "shell", @"C:\cfg\MyShell.exe", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), _pipe, "log.txt",
            _process.StartTime.ToUniversalTime().Ticks, ProcessName: _process.ProcessName);

        var code = await ReloadCommand.QuitForSupervisorAsync(info);

        Assert.True(_shell.ReloadAsked);
        Assert.Equal(0, code);
        await exiting;
    }

    /// <summary>Takes the one request, then exits without answering.</summary>
    private async Task ExitOnRequest(NamedPipeServerStream pipe)
    {
        await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        _process.Kill();
        await _process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await pipe.DisposeAsync();
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit(5000);
        }
        _shell.Dispose();
    }
}
