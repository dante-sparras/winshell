using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell;

internal sealed record IpcRequest(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("target")] string? Target = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("args")] List<string>? Args = null,
    [property: JsonPropertyName("json"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Json = false);

internal sealed record IpcResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("error")] string? Error = null);

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
internal sealed partial class IpcJson : JsonSerializerContext;

/// <summary>Takes CLI requests on a named pipe and runs them on the UI thread.</summary>
internal sealed class IpcServer : IDisposable
{
    private readonly string _pipe;
    private readonly IEventLoop _loop;
    private readonly Func<ShellHost> _host;
    private readonly Action _kill;
    private readonly Action? _dismissNotice;
    private readonly Func<SafePipeHandle, string?> _refusal;
    private readonly CancellationTokenSource _cancel = new();

    private const int WaitingInstances = 2;
    private const int OpenAttempts = 3;

    // A client that never ends its line, and a listener that never reads, must not grow without a limit.
    // The signal callback runs on the UI thread, so a full queue drops the oldest value instead of waiting.
    // A command line holds at most 32767 characters, so a request from winshell is well under this once encoded.
    internal const int MaxRequestBytes = 256 * 1024;
    // A listener holds its client for as long as it runs, so this has to leave room for `winshell kill`.
    internal const int MaxClients = 64;
    internal const int MaxQueuedSignals = 1024;

    private readonly object _firstGate = new();
    private readonly object _limit = new();
    private readonly List<NamedPipeServerStream> _serving = [];
    private NamedPipeServerStream? _first;
    private int _clients;
    private bool _disposed;

    /// <param name="refusal">Why a connected client is refused, or null. Tests replace the integrity check.</param>
    /// <param name="dismissNotice">
    /// Closes Winshell's own notice. <c>ipc call notice dismiss</c> uses it: that notice is not an <c>IpcHandler</c>,
    /// and it is showing when the configuration failed to load, which is when a handler cannot exist.
    /// </param>
    public IpcServer(NamedPipeServerStream first, string pipe, IEventLoop loop, Func<ShellHost> host, Action kill, Func<SafePipeHandle, string?>? refusal = null, Action? dismissNotice = null)
    {
        _pipe = pipe;
        _first = first;
        _loop = loop;
        _host = host;
        _kill = kill;
        _dismissNotice = dismissNotice;
        _refusal = refusal ?? PipeTrust.ClientRefusal;
        _ = Task.Run(AcceptLoop);
    }

    /// <summary>Creates the first instance. Fails if the name is already taken, instead of attaching to it.</summary>
    internal static NamedPipeServerStream OpenFirst(string pipe) => Create(pipe, first: true);

    internal static NamedPipeServerStream OpenNext(string pipe) => Create(pipe, first: false);

    /// <summary>Opens a free pipe, trying a new <c>winshell-</c> id when the name is taken. At most eight tries.</summary>
    internal static NamedPipeServerStream OpenAvailable(ref string pipe)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                return OpenFirst(pipe);
            }
            catch (IOException) when (attempt < 7)
            {
                pipe = "winshell-" + Instances.NewId();
            }
        }
        throw new IOException("The control pipe is already taken.");
    }

    private static NamedPipeServerStream Create(string pipe, bool first)
    {
        var handle = PipeTrust.OpenServer(pipe, first);
        try
        {
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Keeps two instances waiting. One client taking an instance then leaves another free, even while this loop
    /// waits for a thread and has not seen that connection yet. A name with no instance can be created again by
    /// anyone, and this shell's next instance would then join that pipe. So a replacement waits before a connected
    /// instance is handed off, and the loop stops rather than open an instance after it has none left.
    /// </summary>
    private async Task AcceptLoop()
    {
        var waiting = new Dictionary<Task, NamedPipeServerStream>();
        NamedPipeServerStream? first;
        lock (_firstGate)
        {
            first = _first;
            _first = null;
        }
        try
        {
            if (first is null || !StartWaiting(first, waiting))
                return;
            OpenInstances(waiting);
            while (waiting.Count > 0)
            {
                var finished = await Task.WhenAny(waiting.Keys).ConfigureAwait(false);
                waiting.Remove(finished, out var stream);
                if (_cancel.IsCancellationRequested)
                {
                    stream!.Dispose();
                    return;
                }
                OpenInstances(waiting);
                // A client that never finishes its line holds a slot until it leaves or the shell stops.
                // Past the cap the connection is closed, so the wait does not grow with every connect.
                if (finished.IsCompletedSuccessfully && TryTakeClient())
                {
                    var taken = stream!;
                    Track(taken);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Serve(taken).ConfigureAwait(false);
                        }
                        finally
                        {
                            Untrack(taken);
                            ReleaseClient();
                        }
                    });
                }
                else
                    stream!.Dispose();
            }
            Log.Default.Error("ipc", "The control pipe has no instance left, so commands can't reach this shell.");
        }
        finally
        {
            foreach (var stream in waiting.Values)
                stream.Dispose();
        }
    }

    /// <summary>Opens instances until two wait. What still fails here is tried again after the next connection.</summary>
    private void OpenInstances(Dictionary<Task, NamedPipeServerStream> waiting)
    {
        var failures = 0;
        while (waiting.Count < WaitingInstances && failures < OpenAttempts)
        {
            try
            {
                if (!StartWaiting(Create(_pipe, first: false), waiting))
                    failures++;
            }
            catch (IOException e)
            {
                failures++;
                Log.Default.Warn("ipc", $"Could not open the next control pipe instance: {e.Message}");
            }
        }
    }

    private bool StartWaiting(NamedPipeServerStream stream, Dictionary<Task, NamedPipeServerStream> waiting)
    {
        try
        {
            waiting.Add(stream.WaitForConnectionAsync(_cancel.Token), stream);
            return true;
        }
        catch (IOException e)
        {
            Log.Default.Warn("ipc", $"Could not wait on a control pipe instance: {e.Message}");
            stream.Dispose();
            return false;
        }
    }

    private async Task Serve(NamedPipeServerStream stream)
    {
        try
        {
            await using var _ = stream;
            var refusal = _refusal(stream.SafePipeHandle);
            if (refusal is not null)
                Log.Default.Warn("ipc", $"Refused a control pipe client: {refusal}");
            // One byte at a time: a larger read could take bytes from after the newline, and a listener
            // uses those bytes to notice that the client has gone.
            var (line, tooLong) = await ReadRequest(stream, _cancel.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            if (tooLong)
            {
                await Send(writer, new IpcResponse(false, Error: "Request is too long"), _cancel.Token).ConfigureAwait(false);
                return;
            }
            if (line is null)
                return;
            // Answered after the request is read, so the client has finished writing and reads the reason.
            if (refusal is not null)
            {
                await Send(writer, new IpcResponse(false, Error: refusal), _cancel.Token).ConfigureAwait(false);
                return;
            }
            IpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize(line, IpcJson.Default.IpcRequest);
            }
            catch (JsonException)
            {
                request = null;
            }
            if (request is null)
            {
                await Send(writer, new IpcResponse(false, Error: "Malformed request"), _cancel.Token).ConfigureAwait(false);
                return;
            }
            if (request.Op is "listen" or "wait")
            {
                await Listen(request, writer, stream, once: request.Op == "wait").ConfigureAwait(false);
                return;
            }
            var response = await OnUiThread(() => Handle(request)).ConfigureAwait(false);
            await Send(writer, response, _cancel.Token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The client closed the pipe (Ctrl+C, or it exited). That is not a crash.
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancelled the pending read.
        }
    }

    private IpcResponse Handle(IpcRequest request)
    {
        // Before the configuration's handlers: there is no handler when the configuration did not load.
        if (request is { Op: "call", Target: "notice", Name: "dismiss" })
            return DismissNotice(request.Args);
        var ipc = _host().Ipc;
        try
        {
            return request.Op switch
            {
                "show" when request.Json => new IpcResponse(true, CliJson.IpcShow(ipc.Describe())),
                "show" => new IpcResponse(true, ipc.Show()),
                "call" => new IpcResponse(true, ipc.Call(request.Target ?? "", request.Name ?? "", request.Args ?? [])),
                "prop" => new IpcResponse(true, ipc.GetProperty(request.Target ?? "", request.Name ?? "")),
                "kill" => Kill(),
                "reload" => Reload(),
                _ => new IpcResponse(false, Error: $"Unknown request {request.Op}"),
            };
        }
        catch (IpcException e)
        {
            return new IpcResponse(false, Error: e.Message);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A script error is a failed call, not a crash report from an unobserved task.
            return new IpcResponse(false, Error: Log.Describe(e, "ipc"));
        }
    }

    /// <summary><c>winshell ipc call notice dismiss</c>. It takes no arguments and closes whichever notice is showing.</summary>
    private IpcResponse DismissNotice(List<string>? args)
    {
        if (args is { Count: > 0 })
            return new IpcResponse(false, Error: $"Function notice.dismiss takes 0 arguments, {args.Count} given");
        if (_dismissNotice is not { } dismiss)
            return new IpcResponse(false, Error: "Target not found: notice");
        dismiss();
        return new IpcResponse(true);
    }

    private IpcResponse Kill()
    {
        _loop.Post(_kill);
        return new IpcResponse(true);
    }

    private IpcResponse Reload()
    {
        _host().Reload(hard: false);
        return new IpcResponse(true);
    }

    private async Task Listen(IpcRequest request, StreamWriter writer, Stream stream, bool once)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueuedSignals)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        IDisposable? subscription = null;
        var error = await OnUiThread(() =>
        {
            try
            {
                subscription = _host().Ipc.Listen(request.Target ?? "", request.Name ?? "", value => channel.Writer.TryWrite(value));
                return null;
            }
            catch (IpcException e)
            {
                return e.Message;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return Log.Describe(e, "ipc");
            }
        }).ConfigureAwait(false);
        if (error is not null)
        {
            await Send(writer, new IpcResponse(false, Error: error), _cancel.Token).ConfigureAwait(false);
            return;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
        Task<bool>? waiting = null;
        try
        {
            while (true)
            {
                // The read is cancelled before a response is written. A pipe does not take a read and a write at once,
                // and a client that leaves without another signal would otherwise sit here until shutdown.
                using var pause = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var gone = WaitForDisconnect(stream, pause.Token);
                waiting = channel.Reader.WaitToReadAsync(stop.Token).AsTask();
                var done = await Task.WhenAny(gone, waiting).ConfigureAwait(false);
                if (gone.IsCompletedSuccessfully || done == gone)
                {
                    await Finish(gone).ConfigureAwait(false);
                    break;
                }
                pause.Cancel();
                await Finish(gone).ConfigureAwait(false);
                var signal = waiting;
                waiting = null;
                if (!await signal.ConfigureAwait(false))
                    break;
                while (channel.Reader.TryRead(out var value))
                {
                    await Send(writer, new IpcResponse(true, value), _cancel.Token).ConfigureAwait(false);
                    if (once)
                        return;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            stop.Cancel();
            if (waiting is not null)
                await Finish(waiting).ConfigureAwait(false);
            _loop.Post(() => subscription?.Dispose());
        }
    }

    /// <summary>Reads one request line, or reports that it passed <see cref="MaxRequestBytes"/> with no newline.</summary>
    private static async Task<(string? Line, bool TooLong)> ReadRequest(Stream stream, CancellationToken cancel)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(MaxRequestBytes + 1);
        try
        {
            var count = 0;
            while (true)
            {
                var n = await stream.ReadAsync(bytes.AsMemory(count, 1), cancel).ConfigureAwait(false);
                if (n == 0)
                    return (null, false);
                if (bytes[count] == (byte)'\n')
                {
                    if (count > 0 && bytes[count - 1] == (byte)'\r')
                        count--;
                    return (Encoding.UTF8.GetString(bytes, 0, count), false);
                }
                count++;
                if (count > MaxRequestBytes)
                    return (null, true);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Completes when the client closes its end. Bytes it still sends are discarded.</summary>
    private static async Task WaitForDisconnect(Stream stream, CancellationToken cancel)
    {
        var one = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(one.AsMemory(), cancel).ConfigureAwait(false);
            if (n == 0)
                return;
        }
    }

    private static async Task Finish(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private bool TryTakeClient()
    {
        lock (_limit)
        {
            if (_clients >= MaxClients)
                return false;
            _clients++;
            return true;
        }
    }

    private void ReleaseClient()
    {
        lock (_limit)
            _clients--;
    }

    private void Track(NamedPipeServerStream stream)
    {
        lock (_limit)
            _serving.Add(stream);
    }

    private void Untrack(NamedPipeServerStream stream)
    {
        lock (_limit)
            _serving.Remove(stream);
    }

    private Task<T> OnUiThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _loop.Post(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }

    private static Task Send(StreamWriter writer, IpcResponse response, CancellationToken cancel) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcJson.Default.IpcResponse).AsMemory(), cancel);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancel.Cancel();
        NamedPipeServerStream[] open;
        lock (_limit)
            open = [.. _serving];
        foreach (var stream in open)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
        }
        lock (_firstGate)
        {
            _first?.Dispose();
            _first = null;
        }
    }

    /// <summary>Sends one request to an instance and returns its responses, one per line.</summary>
    public static async IAsyncEnumerable<IpcResponse> Request(string pipe, IpcRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel = default)
    {
        // Not CurrentUserOnly: that compares the pipe's owner with the token's default owner, which differs when
        // one side is elevated. ServerRefusal checks the owner against the user instead.
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000, cancel).ConfigureAwait(false);
        if (PipeTrust.ServerRefusal(client) is { } refusal)
            throw new IOException(refusal);
        using var reader = new StreamReader(client, Encoding.UTF8);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, IpcJson.Default.IpcRequest)).ConfigureAwait(false);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancel).ConfigureAwait(false);
            if (line is null)
                yield break;
            var response = JsonSerializer.Deserialize(line, IpcJson.Default.IpcResponse);
            if (response is not null)
                yield return response;
        }
    }
}
