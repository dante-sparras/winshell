using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Tests;

/// <summary>
/// Bounds on the control pipe. Clients are this process talking to a pipe created here, not a shell running on the PC.
/// </summary>
public class IpcBoundsTests
{
    [Fact]
    public async Task An_unfinished_line_stops_at_the_limit_and_shutdown_cancels_the_read()
    {
        var name = PipeName();
        var loop = new EventQueue();
        var killed = false;
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, loop, () => null!, () => killed = true);
        await using var client = await Connect(name);

        var payload = new byte[IpcServer.MaxRequestBytes + 1];
        Array.Fill(payload, (byte)'a');
        var writing = Task.Run(() =>
        {
            try
            {
                client.Write(payload);
            }
            catch (IOException)
            {
            }
        });
        using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: true);
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(line);
        var response = JsonSerializer.Deserialize(line, IpcJson.Default.IpcResponse);
        Assert.NotNull(response);
        Assert.False(response.Ok);
        Assert.Equal("Request is too long", response.Error);
        await writing.WaitAsync(TimeSpan.FromSeconds(5));

        await using var parked = await Connect(name);
        var held = new byte[IpcServer.MaxRequestBytes - 1];
        var prefix = "{\"op\":\"kill\""u8;
        prefix.CopyTo(held);
        Array.Fill(held, (byte)' ', prefix.Length, held.Length - prefix.Length);
        await parked.WriteAsync(held);
        server.Dispose();

        var n = await parked.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, n);
        try
        {
            await parked.WriteAsync("}\n"u8.ToArray());
        }
        catch (IOException)
        {
        }
        loop.RunDue();
        Assert.False(killed);
    }

    [Fact]
    public async Task A_disconnected_listener_is_released_without_a_later_signal()
    {
        using var probe = new Probe();
        await using var client = await Connect(probe.Pipe);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"op\":\"listen\",\"target\":\"probe\",\"name\":\"ping\"}\n"));
        await probe.WaitUntil(() => probe.Ping.Count == 1, "The listener did not subscribe.");

        await client.DisposeAsync();

        await probe.WaitUntil(() => probe.Ping.Count == 0, "The listener stayed after the client left, with no signal.");
    }

    [Fact]
    public async Task A_connection_past_the_cap_is_closed()
    {
        using var probe = new Probe();
        var held = new List<NamedPipeClientStream>();
        try
        {
            var request = Encoding.UTF8.GetBytes("{\"op\":\"listen\",\"target\":\"probe\",\"name\":\"ping\"}\n");
            for (var i = 0; i < IpcServer.MaxClients; i++)
            {
                var client = await Connect(probe.Pipe);
                held.Add(client);
                await client.WriteAsync(request);
                var subscribed = i + 1;
                await probe.WaitUntil(() => probe.Ping.Count == subscribed, "A listener inside the cap did not subscribe.");
            }

            await using var extra = await Connect(probe.Pipe);
            var n = await extra.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, n);
            Assert.Equal(IpcServer.MaxClients, probe.Ping.Count);
        }
        finally
        {
            foreach (var client in held)
                await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_listener_that_falls_behind_keeps_only_the_signal_buffer()
    {
        using var probe = new Probe();
        await using var client = await Connect(probe.Pipe);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"op\":\"listen\",\"target\":\"probe\",\"name\":\"ping\"}\n"));
        await probe.WaitUntil(() => probe.Ping.Count == 1, "The listener did not subscribe.");

        // Larger than the pipe buffer, so the server stays inside this write until the client reads it.
        var value = new string('a', 128 * 1024);
        probe.Ping.Emit([value]);
        var first = new byte[1];
        Assert.Equal(1, await client.ReadAsync(first).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        // Short, so the test reads a full queue quickly. Only the first has to hold the server in its write.
        for (var i = 0; i < IpcServer.MaxQueuedSignals + 20; i++)
            probe.Ping.Emit(["queued"]);

        var lines = await ReadLines(client, 1 + IpcServer.MaxQueuedSignals, TimeSpan.FromSeconds(30), first[0]);
        Assert.Equal(1 + IpcServer.MaxQueuedSignals, lines.Count);
        var values = lines.Select(line =>
        {
            var response = JsonSerializer.Deserialize(line, IpcJson.Default.IpcResponse);
            Assert.NotNull(response);
            Assert.True(response.Ok);
            return response.Value;
        }).ToList();
        Assert.Equal(value, values[0]);
        Assert.All(values.Skip(1), queued => Assert.Equal("queued", queued));
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static string PipeName() => "winshell-test-" + Guid.NewGuid().ToString("N");

    private static async Task<NamedPipeClientStream> Connect(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        return client;
    }

    private static async Task<List<string>> ReadLines(Stream stream, int count, TimeSpan timeout, byte already)
    {
        var pending = new List<byte> { already };
        var lines = new List<string>();
        var buf = new byte[8192];
        var until = DateTime.UtcNow + timeout;
        while (lines.Count < count)
        {
            var remaining = until - DateTime.UtcNow;
            Assert.True(remaining > TimeSpan.Zero, $"The listener delivered {lines.Count} lines, not {count}.");
            var n = await stream.ReadAsync(buf).AsTask().WaitAsync(remaining);
            Assert.NotEqual(0, n);
            for (var i = 0; i < n; i++)
            {
                if (buf[i] == (byte)'\n')
                {
                    lines.Add(Encoding.UTF8.GetString(pending.ToArray()));
                    pending.Clear();
                    if (lines.Count == count)
                        break;
                }
                else if (buf[i] != (byte)'\r')
                    pending.Add(buf[i]);
            }
        }
        return lines;
    }

    /// <summary>A shell in this process, with one signal, and its control pipe. Nothing is installed or replaced.</summary>
    private sealed class Probe : IDisposable
    {
        private readonly string _dir;

        public Probe()
        {
            _dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var root = Path.Combine(_dir, "shell.qml");
            File.WriteAllText(root, $$"""
                //@ pragma DataDir {{Path.Combine(_dir, "data")}}
                //@ pragma StateDir {{Path.Combine(_dir, "state")}}
                //@ pragma CacheDir {{Path.Combine(_dir, "cache")}}
                import QtQuick
                import Winshell
                import Winshell.Io
                ShellRoot {
                  IpcHandler {
                    target: "probe"
                    signal ping(value: string)
                  }
                }
                """);
            var errors = new List<string>();
            var log = new Log { MinimumLevel = LogLevel.Error, Sink = (_, category, message) => errors.Add(category + ": " + message) };
            Loop = new ManualEventLoop();
            Host = new ShellHost(root, Loop, null, log) { WatchFiles = false };
            Assert.True(Host.Start(), string.Join("\n", errors));
            Loop.Process();
            var shell = Assert.IsType<ShellRoot>(Host.Root);
            var handler = Assert.Single(shell.Children.Snapshot.OfType<IpcHandler>());
            Ping = handler.GetSignal("ping")!;
            Assert.NotNull(Ping);
            Pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
            Server = new IpcServer(IpcServer.OpenFirst(Pipe), Pipe, Loop, () => Host, () => { });
        }

        public ManualEventLoop Loop { get; }

        public ShellHost Host { get; }

        public IpcServer Server { get; }

        public string Pipe { get; }

        public SignalSlot Ping { get; }

        public async Task WaitUntil(Func<bool> ready, string why)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!ready())
            {
                Loop.Process();
                Assert.True(DateTime.UtcNow < until, why);
                await Task.Delay(15);
            }
        }

        public void Dispose()
        {
            Server.Dispose();
            Loop.Process();
            Host.Dispose();
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
