using System.Globalization;
using System.IO.Pipes;
using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class SocketServerRestartTests
{
    [Fact]
    public async Task A_restart_does_not_accept_on_the_pipe_it_left()
    {
        var first = "winshell-n3-" + Guid.NewGuid().ToString("N");
        var second = "winshell-n3-" + Guid.NewGuid().ToString("N");
        using var shell = new TestShell(("shell.qml", """
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              id: root
              property string pipe: ""
              property int accepted: 0
              SocketServer {
                path: pipe
                active: pipe.length > 0
                handler: Socket {
                  Component.onCompleted: root.accepted = root.accepted + 1
                }
              }
            }
            """));
        var root = shell.Load();

        root.WriteProperty("pipe", @"\\.\pipe\" + first);
        Assert.True(await Connect(shell, first, 30_000));
        await Wait.UntilAsync(() => Accepted(root) == 1, "the first client", shell.Loop.Process);
        Assert.Equal(1, Accepted(root));

        root.WriteProperty("pipe", @"\\.\pipe\" + second);
        await Wait.UntilAsync(() => Listening(second) && !Listening(first), "the server to move to the new pipe", shell.Loop.Process);

        await Connect(shell, first, 400);
        Wait.ForNothingToHappen(TimeSpan.FromMilliseconds(500));
        shell.Loop.Process();
        Assert.Equal(1, Accepted(root));

        Assert.True(await Connect(shell, second, 30_000));
        await Wait.UntilAsync(() => Accepted(root) == 2, "the second client", shell.Loop.Process);
        Assert.Equal(2, Accepted(root));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("SocketServer", StringComparison.Ordinal));
    }

    /// <summary>Whether the pipe is open. This lists pipes, so unlike connecting it does not use one up.</summary>
    private static bool Listening(string pipe) => Directory.EnumerateFiles(@"\\.\pipe\").Any(path => path.EndsWith(pipe, StringComparison.Ordinal));

    private static int Accepted(QObject root) => Convert.ToInt32(root.ReadProperty("accepted"), CultureInfo.InvariantCulture);

    private static async Task<bool> Connect(TestShell shell, string pipe, int timeoutMs)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connect = client.ConnectAsync(timeoutMs);
        // The accept is posted from the listen thread, so the loop has to run after the client connects.
        await Wait.UntilAsync(() => connect.IsCompleted, "the connection to finish", shell.Loop.Process);
        shell.Loop.Process();
        try
        {
            await connect;
            return client.IsConnected;
        }
        catch (Exception e) when (e is TimeoutException or IOException or OperationCanceledException)
        {
            return false;
        }
    }
}
