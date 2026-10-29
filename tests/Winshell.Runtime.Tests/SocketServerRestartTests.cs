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
        Assert.True(await Connect(shell, first, 3000));
        await Pump(shell, 3000, () => Accepted(root) == 1);
        Assert.Equal(1, Accepted(root));

        root.WriteProperty("pipe", @"\\.\pipe\" + second);
        await Pump(shell, 300, () => false);

        await Connect(shell, first, 400);
        await Pump(shell, 500, () => Accepted(root) != 1);
        Assert.Equal(1, Accepted(root));

        Assert.True(await Connect(shell, second, 3000));
        await Pump(shell, 3000, () => Accepted(root) == 2);
        Assert.Equal(2, Accepted(root));
        Assert.DoesNotContain(shell.Messages, m => m.Contains("SocketServer", StringComparison.Ordinal));
    }

    /// <summary>The accept is posted from the listen thread, so the loop has to run after the client connects.</summary>
    private static async Task Pump(TestShell shell, int timeoutMs, Func<bool> done)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (!done() && Environment.TickCount64 < until)
        {
            shell.Loop.Process();
            await Task.Delay(15);
        }
        shell.Loop.Process();
    }

    private static int Accepted(QObject root) => Convert.ToInt32(root.ReadProperty("accepted"));

    private static async Task<bool> Connect(TestShell shell, string pipe, int timeoutMs)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connect = client.ConnectAsync(timeoutMs);
        var until = Environment.TickCount64 + timeoutMs + 500;
        while (!connect.IsCompleted && Environment.TickCount64 < until)
        {
            shell.Loop.Process();
            await Task.Delay(15);
        }
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
