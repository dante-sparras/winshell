using System.IO.Pipes;
using System.Text;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A socket read that finishes a 4-byte UTF-8 character on a full buffer still delivers the text.</summary>
public class SocketReadTests
{
    [Fact]
    public async Task A_character_completed_on_a_full_read_is_delivered()
    {
        var name = "winshell-socket-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var shell = new TestShell();
        var socket = new SocketObject { Engine = shell.Engine, Path = @"\\.\pipe\" + name };
        var parser = new SplitParser { Engine = shell.Engine, SplitMarker = "" };
        var received = new StringBuilder();
        parser.Connect("read", args => received.Append((string)args[0]!));
        socket.Parser = parser;
        socket.Connected = true;

        var connected = server.WaitForConnectionAsync();
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!socket.Connected && DateTime.UtcNow < until)
        {
            shell.Loop.Process();
            await Task.Delay(20);
        }
        await connected;
        Assert.True(socket.Connected);

        var emoji = Encoding.UTF8.GetBytes("🎵");
        Assert.Equal(4, emoji.Length);
        await server.WriteAsync(emoji.AsMemory(0, 3));
        await server.FlushAsync();
        await Task.Delay(150);
        shell.Loop.Process();

        var chunk = new byte[8192];
        chunk[0] = emoji[3];
        Array.Fill(chunk, (byte)'x', 1, 8191);
        await server.WriteAsync(chunk);
        await server.FlushAsync();

        var expect = "🎵" + new string('x', 8191);
        until = DateTime.UtcNow.AddSeconds(5);
        while (received.Length < expect.Length && DateTime.UtcNow < until)
        {
            shell.Loop.Process();
            await Task.Delay(20);
        }

        Assert.Equal(expect, received.ToString());
        Assert.True(socket.Connected);
    }
}
