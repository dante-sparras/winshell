using System.IO.Pipes;
using System.Runtime.InteropServices;
using Winshell.Runtime;

namespace Winshell.Tests;

/// <summary>The IPC accept loop closes its pipe when the wait ends, and the client checks the pipe's owner.</summary>
public sealed class IpcAcceptTests
{
    [Fact]
    public async Task Cancelling_accept_closes_the_listening_pipe()
    {
        var name = "winshell-accept-" + Guid.NewGuid().ToString("N");
        var server = new IpcServer(name, new EventQueue(), () => throw new InvalidOperationException("no host"), static () => { });
        Assert.True(await UntilAsync(() => PipeExists(name) == true), "the accept pipe did not open");

        server.Dispose();

        Assert.True(await UntilAsync(() => PipeExists(name) == false), "the accept pipe stayed open after dispose");
    }

    [Fact]
    public async Task A_client_limited_to_the_current_user_can_connect()
    {
        var name = "winshell-accept-" + Guid.NewGuid().ToString("N");
        var server = new IpcServer(name, new EventQueue(), () => throw new InvalidOperationException("no host"), static () => { });
        try
        {
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(5000);
            Assert.True(client.IsConnected);
        }
        finally
        {
            server.Dispose();
        }
    }

    private static async Task<bool> UntilAsync(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (ready())
                return true;
            await Task.Delay(30);
        }
        return ready();
    }

    /// <summary>True when a pipe of this name exists, false when Windows says it does not.</summary>
    private static bool PipeExists(string name)
    {
        if (WaitNamedPipe(@"\\.\pipe\" + name, 0))
            return true;
        // 2 is ERROR_FILE_NOT_FOUND. A busy or timed-out pipe still exists.
        return Marshal.GetLastWin32Error() != 2;
    }

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipe(string name, int timeout);
}
