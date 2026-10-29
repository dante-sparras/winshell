using System.IO.Pipes;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public class SocketListenerStartupTests
{
    [Fact]
    public void An_active_pipe_is_ready_when_the_path_setter_returns()
    {
        using var shell = new TestShell(("shell.qml", """
            import Winshell
            import Winshell.Io
            Scope {
                property alias server: server
                SocketServer { id: server; active: true }
            }
            """));
        var root = shell.Load();
        var server = Assert.IsType<SocketServer>(root.ReadProperty("server"));

        for (var i = 0; i < 20; i++)
        {
            var name = "winshell-startup-" + Guid.NewGuid().ToString("N");
            server.Path = @"\\.\pipe\" + name;
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(0);
            Assert.True(client.IsConnected);
            shell.Loop.Process();
        }
        Assert.Empty(shell.Messages);
    }
}
