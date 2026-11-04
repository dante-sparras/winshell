using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Winshell.TestSupport;

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
        var control = new Control();
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, loop, control);
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
        using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: true);
        var line = await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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

        var n = await parked.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, n);
        try
        {
            await parked.WriteAsync("}\n"u8.ToArray(), TestContext.Current.CancellationToken);
        }
        catch (IOException)
        {
        }
        loop.RunDue();
        Assert.False(control.Killed);
    }

    [Fact]
    public async Task A_disconnected_listener_is_released_without_a_later_signal()
    {
        using var probe = new Probe();
        await using var client = await Connect(probe.Pipe);
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"op\":\"listen\",\"target\":\"probe\",\"name\":\"ping\"}\n"), TestContext.Current.CancellationToken);
        await probe.WaitUntil(() => probe.Ping.ListenerCount == 1, "The listener did not subscribe.");

        await client.DisposeAsync();

        await probe.WaitUntil(() => probe.Ping.ListenerCount == 0, "The listener stayed after the client left, with no signal.");
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
                await client.WriteAsync(request, TestContext.Current.CancellationToken);
                var subscribed = i + 1;
                await probe.WaitUntil(() => probe.Ping.ListenerCount == subscribed, "A listener inside the cap did not subscribe.");
            }

            await using var extra = await Connect(probe.Pipe);
            var n = await extra.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, n);
            Assert.Equal(IpcServer.MaxClients, probe.Ping.ListenerCount);
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
        var time = new FakeTimeProvider();
        var loop = new ManualEventLoop();
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, loop, new Control(), time: time);
        var idle = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < IpcServer.MaxClients; i++)
                idle.Add(await ConnectWhenFree(name));

            // The server starts each timer as it takes its client, which this test cannot see. So it advances until a request is answered.
            // The request runs on the loop, which this test pumps between checks.
            var answered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var asking = Task.CompletedTask;
            await Wait.UntilAsync(() => answered.Task.IsCompleted, "The idle clients never gave their slots back.", () =>
            {
                time.Advance(IpcServer.RequestTimeout);
                loop.Process();
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
        await client.WriteAsync(Encoding.UTF8.GetBytes("{\"op\":\"listen\",\"target\":\"probe\",\"name\":\"ping\"}\n"), TestContext.Current.CancellationToken);
        await probe.WaitUntil(() => probe.Ping.ListenerCount == 1, "The listener did not subscribe.");

        // Larger than the pipe buffer, so the server stays inside this write until the client reads it.
        var value = new string('a', 128 * 1024);
        probe.Ping.Emit(value);
        var first = new byte[1];
        Assert.Equal(1, await client.ReadAsync(first, TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
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
            await client.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    private static string PipeName() => "winshell-test-" + Guid.NewGuid().ToString("N");

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
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
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

    /// <summary>One signal and its control pipe, in this process. Nothing is installed or replaced.</summary>
    private sealed class Probe : IInstanceControl, IDisposable
    {
        private readonly ShellIpc _ipc = new(Log.Default);

        public Probe()
        {
            Ping = _ipc.Signal("probe", "ping");
            Loop = new ManualEventLoop();
            Pipe = "winshell-test-" + Guid.NewGuid().ToString("N");
            Server = new IpcServer(IpcServer.OpenFirst(Pipe), Pipe, Loop, this);
        }

        public ManualEventLoop Loop { get; }

        public IpcServer Server { get; }

        public string Pipe { get; }

        public IpcSignal Ping { get; }

        public ShellIpc Ipc => _ipc;

        public string? Reload(string? program) => null;

        public void Quit()
        {
        }

        public Task WaitUntil(Func<bool> ready, string why) => Wait.UntilAsync(ready, why, Loop.Process);

        public void Dispose()
        {
            Server.Dispose();
            Loop.Process();
        }
    }

    private sealed class Control : IInstanceControl
    {
        public bool Killed { get; private set; }

        public ShellIpc Ipc { get; } = new ShellIpc(Log.Default);

        public string? Reload(string? program) => null;

        public void Quit() => Killed = true;
    }
}
