using System.IO.Pipes;
using System.Text;
using Winshell.Runtime.Shell;

#pragma warning disable CA1416 // Winshell only runs on Windows; this project targets plain net10.0.

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

        var connected = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => socket.Connected, "the socket to connect", shell.Loop.Process);
        await connected;
        Assert.True(socket.Connected);

        var emoji = Encoding.UTF8.GetBytes("🎵");
        Assert.Equal(4, emoji.Length);
        await server.WriteAsync(emoji.AsMemory(0, 3), TestContext.Current.CancellationToken);
        await server.FlushAsync(TestContext.Current.CancellationToken);
        // The socket has to have read the first three bytes alone, or the next read is not a full one that ends them.
        await Task.Run(server.WaitForPipeDrain, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        shell.Loop.Process();

        var chunk = new byte[8192];
        chunk[0] = emoji[3];
        Array.Fill(chunk, (byte)'x', 1, 8191);
        await server.WriteAsync(chunk, TestContext.Current.CancellationToken);
        await server.FlushAsync(TestContext.Current.CancellationToken);

        var expect = "🎵" + new string('x', 8191);
        await Wait.UntilAsync(() => received.Length >= expect.Length, "the text", shell.Loop.Process);

        Assert.Equal(expect, received.ToString());
        Assert.True(socket.Connected);
    }
}
