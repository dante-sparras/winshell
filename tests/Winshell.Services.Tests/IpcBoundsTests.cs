using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Winshell.Services.Tests;

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
        var control = new FakeInstanceControl();
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, loop, Log.Default, control);
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
        }, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(client, Encoding.UTF8, false, 256, true);
        var line = await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotNull(line);
        var response = JsonSerializer.Deserialize(line, IpcJson.Default.IpcResponse);
        Assert.NotNull(response);
        Assert.False(response.Ok);
        Assert.Equal("Request is too long.", response.Error);
        await writing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await using var parked = await Connect(name);
        var held = new byte[IpcServer.MaxRequestBytes - 1];
        var prefix = "{\"op\":\"kill\""u8;
        prefix.CopyTo(held);
        Array.Fill(held, (byte)' ', prefix.Length, held.Length - prefix.Length);
        await parked.WriteAsync(held, TestContext.Current.CancellationToken);
        server.Dispose();

        var n = await parked.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, n);
        try
        {
            await parked.WriteAsync("}\n"u8.ToArray(), TestContext.Current.CancellationToken);
        }
        catch (IOException)
        {
        }

        loop.RunDue();
        Assert.Equal(0, control.Quits);
    }

    [Fact]
    public async Task A_listener_that_leaves_without_a_signal_frees_its_place()
    {
        using var probe = new Probe();
        var held = new List<NamedPipeClientStream>();
        try
        {
            await Fill(probe, held, IpcServer.MaxClients);
            await held[0].DisposeAsync();

            // The place is free once the server sees the client gone, with no signal to tell it. Until then a new client is closed.
            var taken = false;
            await Wait.UntilAsync(() => taken, "The place of the listener that left was never freed.", async () =>
            {
                try
                {
                    await using var next = await Connect(probe.Pipe);
                    await next.WriteAsync(ListenRequest, TestContext.Current.CancellationToken);
                    var line = ReadLine(next);
                    await probe.WaitUntil(() =>
                    {
                        probe.Ping.Emit("sync");
                        return line.IsCompleted;
                    }, "A client after the listener left was neither served nor closed.");
                    taken = await line is not null;
                }
                catch (IOException)
                {
                    // At the cap the server can close the pipe before this probe finishes writing or reading.
                }
            });
        }
        finally
        {
            foreach (var client in held)
                await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_connection_past_the_cap_is_closed()
    {
        using var probe = new Probe();
        var held = new List<NamedPipeClientStream>();
        try
        {
            await Fill(probe, held, IpcServer.MaxClients);

            await using var extra = await Connect(probe.Pipe);
            var n = await extra.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, n);
        }
        finally
        {
            foreach (var client in held)
                await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task Clients_that_send_nothing_give_their_slots_back()
    {
        var name = PipeName();
        var loop = new ManualEventLoop();
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, loop, Log.Default, new FakeInstanceControl());
        var idle = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < IpcServer.MaxClients; i++)
                idle.Add(await ConnectWhenFree(name));

            // The server starts each timer as it takes its client, which this test cannot see. So it advances until a request is answered.
            // The request runs on the loop, which this test pumps between checks.
            var answered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var asking = Task.CompletedTask;
            await Wait.UntilAsync(() => answered.Task.IsCompleted, "The idle clients never gave their slots back.",
                () =>
                {
                    loop.Advance(IpcServer.RequestTimeout);
                    if (asking.IsCompleted)
                        asking = Ask(name, answered);
                });
            var answer = await answered.Task;
            var response = JsonSerializer.Deserialize(answer, IpcJson.Default.IpcResponse);
            Assert.NotNull(response);
            Assert.True(response.Ok);
        }
        finally
        {
            foreach (var client in idle)
                await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_listener_that_falls_behind_keeps_only_the_signal_buffer()
    {
        using var probe = new Probe();
        await using var client = await Connect(probe.Pipe);
        await Subscribe(probe, client);

        // Larger than the pipe buffer, so the server stays inside this write until the client reads it.
        var value = new string('a', 128 * 1024);
        probe.Ping.Emit(value);
        var first = new byte[1];
        Assert.Equal(1,
            await client.ReadAsync(first, TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        // Short, so the test reads a full queue quickly. Only the first has to hold the server in its write.
        for (var i = 0; i < IpcServer.MaxQueuedSignals + 20; i++)
            probe.Ping.Emit("queued");

        var lines = await ReadLines(client, 1 + IpcServer.MaxQueuedSignals, first[0]);
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
            await client.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    private static readonly byte[] ListenRequest =
        Encoding.UTF8.GetBytes("{\"op\":\"listen\",\"target\":\"probe\",\"name\":\"ping\"}\n");

    /// <summary>
    /// Listens, then returns once the server is following the signal and the stream is empty. A signal before that is
    /// lost, so this emits until one arrives, then emits a last one and reads up to it.
    /// </summary>
    private static async Task Subscribe(Probe probe, Stream client)
    {
        await client.WriteAsync(ListenRequest, TestContext.Current.CancellationToken);
        var line = ReadLine(client);
        await probe.WaitUntil(() =>
        {
            probe.Ping.Emit("sync");
            return line.IsCompleted;
        }, "The listener did not subscribe.");
        probe.Ping.Emit("done");
        var value = await line;
        while (value != "done")
        {
            Assert.NotNull(value);
            value = await ReadLine(client);
        }
    }

    /// <summary>Holds <paramref name="count"/> listeners, one after the other, so each is served before the next connects.</summary>
    private static async Task Fill(Probe probe, List<NamedPipeClientStream> held, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var client = await Connect(probe.Pipe);
            held.Add(client);
            await Subscribe(probe, client);
        }
    }

    /// <summary>The value of the next response line, or null when the server closed the stream.</summary>
    private static async Task<string?> ReadLine(Stream stream)
    {
        var line = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(one, TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (n == 0)
                return null;
            if (one[0] != (byte)'\n')
            {
                line.Add(one[0]);
                continue;
            }

            var response = JsonSerializer.Deserialize(line.ToArray(), IpcJson.Default.IpcResponse);
            Assert.NotNull(response);
            Assert.True(response.Ok);
            return response.Value;
        }
    }

    private static string PipeName()
    {
        return "winshell-test-" + Guid.NewGuid().ToString("N");
    }

    private static async Task<NamedPipeClientStream> Connect(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        return client;
    }

    private static async Task Ask(string name, TaskCompletionSource<string> answered)
    {
        try
        {
            await using var client = await Connect(name);
            await client.WriteAsync("{\"op\":\"show\"}\n"u8.ToArray());
            using var reader = new StreamReader(client, Encoding.UTF8, false, leaveOpen: true);
            if (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)) is { } line)
                answered.TrySetResult(line);
        }
        catch (Exception e) when (e is IOException or TimeoutException)
        {
            // The server was full and closed this connection. The next try comes after more time passes.
        }
    }

    /// <summary>A burst of connects can find both waiting instances taken, so each try is short and the next one looks again.</summary>
    private static async Task<NamedPipeClientStream> ConnectWhenFree(string name)
    {
        NamedPipeClientStream? client = null;
        await Wait.UntilAsync(() => client is not null, "The pipe never had a free instance.", () =>
        {
            var attempt = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                attempt.Connect(200);
                client = attempt;
            }
            catch (TimeoutException)
            {
                attempt.Dispose();
            }
        });
        return client!;
    }

    private static async Task<List<string>> ReadLines(Stream stream, int count, byte already)
    {
        var pending = new List<byte> { already };
        var lines = new List<string>();
        var buf = new byte[8192];
        while (lines.Count < count)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(buf, TestContext.Current.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"The listener delivered {lines.Count} lines, not {count}.");
                throw;
            }

            Assert.NotEqual(0, n);
            for (var i = 0; i < n; i++)
                if (buf[i] == (byte)'\n')
                {
                    lines.Add(Encoding.UTF8.GetString(pending.ToArray()));
                    pending.Clear();
                    if (lines.Count == count)
                        break;
                }
                else if (buf[i] != (byte)'\r')
                {
                    pending.Add(buf[i]);
                }
        }

        return lines;
    }

    /// <summary>One signal and its control pipe, in this process. Nothing is installed or replaced.</summary>
    private sealed class Probe : IDisposable
    {
        public Probe()
        {
            var control = new FakeInstanceControl();
            Ping = control.Ipc.Signal("probe", "ping");
            Loop = new ManualEventLoop();
            Pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
            Server = new IpcServer(IpcServer.OpenFirst(Pipe), Pipe, Loop, Log.Default, control);
        }

        public ManualEventLoop Loop { get; }

        public IpcServer Server { get; }

        public string Pipe { get; }

        public IpcSignal Ping { get; }

        public Task WaitUntil(Func<bool> ready, string why)
        {
            return Wait.UntilAsync(ready, why, Loop.Process);
        }

        public void Dispose()
        {
            Server.Dispose();
            Loop.Process();
        }
    }
}
