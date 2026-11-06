using System.IO.Pipes;
using System.Text;

namespace Winshell.Tests;

/// <summary>
/// A call to an instance that fails says which instance and why. The test is the shell: it serves the pipe here, so no
/// instance on this PC is called.
/// </summary>
public class InstanceCallTests
{
    private readonly string _pipe = "winshell-test-" + Guid.NewGuid().ToString("N");

    private InstanceInfo Instance =>
        new("abc123", Environment.ProcessId, "shell", @"C:\cfg\MyShell.exe",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), _pipe, "log.txt");

    // Whether the close lands before or after the call starts reading varies, so the call is made several times.
    [Fact]
    public async Task A_call_to_an_instance_that_closes_the_pipe_without_answering_says_so()
    {
        for (var call = 0; call < 10; call++)
        {
            await using var shell = IpcServer.OpenFirst(_pipe);
            var closing = ReadThen(shell, shell.Dispose);

            var answer = await InstanceCall.AskAsync(Instance, IpcRequest.ForShow(false),
                TestContext.Current.CancellationToken);

            Assert.Equal(new InstanceCall.Answer(null, "Instance abc123 closed the control pipe without answering."),
                answer);
            await closing;
        }
    }

    [Fact]
    public async Task A_call_this_winshell_rejects_names_the_instance_and_the_reason()
    {
        await using var shell = IpcServer.OpenFirst(_pipe);
        var other = ProductVersion.Current.Major == 9 ? "1.0.0" : "9.9.9";
        var answering = ReadThen(shell, () =>
        {
            using var writer = new StreamWriter(shell, new UTF8Encoding(false), leaveOpen: true);
            writer.WriteLine($$"""{"ok":true,"winshell":"{{other}}"}""");
        });

        var answer =
            await InstanceCall.AskAsync(Instance, IpcRequest.ForShow(false), TestContext.Current.CancellationToken);

        Assert.Equal(
            new InstanceCall.Answer(null,
                $"Rejected instance abc123: {IpcProtocol.ReleaseRefusal(other, ProductVersion.Current.Text)}"),
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
