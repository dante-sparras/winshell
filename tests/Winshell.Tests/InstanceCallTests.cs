using System.IO.Pipes;
using System.Text;

namespace Winshell.Tests;

/// <summary>
/// A request kill, reload, ipc or inspect sends that gets no answer says why, in the same words for each. The test is the
/// shell: it serves the pipe here, so no instance on this PC is called.
/// </summary>
public class InstanceCallTests
{
    private readonly string _pipe = "winshell-test-" + Guid.NewGuid().ToString("N");

    private InstanceInfo Instance =>
        new("abc123", Environment.ProcessId, "shell", @"C:\cfg\MyShell.exe", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), _pipe, "log.txt");

    [Fact]
    public async Task An_instance_this_winshell_refuses_could_not_be_reached_and_says_why()
    {
        await using var shell = IpcServer.OpenFirst(_pipe);
        var other = ProductVersion.Current.Major == 9 ? "1.0.0" : "9.9.9";
        var answering = ReadThen(shell, () =>
        {
            using var writer = new StreamWriter(shell, new UTF8Encoding(false), leaveOpen: true);
            writer.WriteLine($$"""{"ok":true,"winshell":"{{other}}"}""");
        });

        var answer = await InstanceCall.AskAsync(Instance, new IpcRequest("show"));

        Assert.Equal(
            new InstanceAnswer(null, $"Could not reach instance abc123: {IpcProtocol.ReleaseRefusal(other, ProductVersion.Current.Text)}"),
            answer);
        await answering;
    }

    /// <summary>Takes the one request, then does what the shell does next.</summary>
    private static async Task ReadThen(NamedPipeServerStream shell, Action next)
    {
        await shell.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        var reader = new StreamReader(shell, Encoding.UTF8, leaveOpen: true);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        next();
    }
}
