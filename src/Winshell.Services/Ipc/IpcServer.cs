using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace Winshell;

/// <summary>Takes CLI requests on a named pipe and runs them on the UI thread.</summary>
internal sealed class IpcServer : IDisposable
{
    private readonly string _pipe;
    private readonly IEventLoop _loop;
    private readonly IInstanceControl _control;
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

    // winshell sends its line as soon as it connects. A client that does not, refused or not, gives its slot back.
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly object _firstGate = new();
    private readonly object _limit = new();
    private readonly List<NamedPipeServerStream> _serving = [];
    private NamedPipeServerStream? _first;
    private int _clients;
    private bool _disposed;

    /// <param name="first">
    /// The pipe instance <see cref="OpenFirst"/> or <see cref="OpenAvailable"/> opened, which the server now owns.
    /// </param>
    /// <param name="pipe">The pipe's name, for the instances opened after the first.</param>
    /// <param name="loop">The UI thread's loop, which requests run on, and whose clock times <see cref="RequestTimeout"/>.</param>
    /// <param name="control">What requests do to the shell.</param>
    /// <param name="refusal">
    /// Why a connected client is refused, or null. Tests replace the integrity check: a client below the shell's
    /// integrity is a process a test can't start.
    /// </param>
    public IpcServer(NamedPipeServerStream first, string pipe, IEventLoop loop, IInstanceControl control,
        Func<SafePipeHandle, string?>? refusal = null)
    {
        _pipe = pipe;
        _first = first;
        _loop = loop;
        _control = control;
        _refusal = refusal ?? PipeTrust.ClientRefusal;
        _ = Task.Run(AcceptLoop);
    }

    /// <summary>Creates the first instance. Fails if the name is already taken, instead of attaching to it.</summary>
    internal static NamedPipeServerStream OpenFirst(string pipe)
    {
        return Create(pipe, true);
    }

    /// <summary>Creates another instance of a pipe this process already serves.</summary>
    internal static NamedPipeServerStream OpenNext(string pipe)
    {
        return Create(pipe, false);
    }

    /// <summary>
    /// The names a start tries, in order: <paramref name="pipe"/>, then that name with <c>-2</c> through <c>-8</c>.
    /// </summary>
    internal static IReadOnlyList<string> Candidates(string pipe)
    {
        var names = new string[8];
        names[0] = pipe;
        for (var n = 1; n < names.Length; n++)
            names[n] = pipe + "-" + (n + 1);
        return names;
    }

    /// <summary>
    /// Opens the first free name from <see cref="Candidates"/>. The chosen name is written back to <paramref name="pipe"/>.
    /// Only a taken name moves on to the next: any other failure fails on every name.
    /// </summary>
    internal static NamedPipeServerStream OpenAvailable(ref string pipe)
    {
        var names = Candidates(pipe);
        for (var attempt = 0; attempt < names.Count - 1; attempt++)
            try
            {
                pipe = names[attempt];
                return OpenFirst(pipe);
            }
            catch (PipeTakenException)
            {
            }

        pipe = names[^1];
        return OpenFirst(pipe);
    }

    private static NamedPipeServerStream Create(string pipe, bool first)
    {
        var handle = PipeTrust.OpenServer(pipe, first);
        try
        {
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
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
                {
                    stream!.Dispose();
                }
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
            try
            {
                if (!StartWaiting(OpenNext(_pipe), waiting))
                    failures++;
            }
            catch (IOException e)
            {
                failures++;
                Log.Default.Warn("ipc", $"Could not open the next control pipe instance: {e.Message}");
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
            // Only the request line is timed: a listener holds its client for as long as it runs.
            string? line;
            bool tooLong;
            using (var idle = new CancellationTokenSource(RequestTimeout, _loop.Time))
            using (var reading = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token, idle.Token))
            {
                (line, tooLong) = await ReadRequest(stream, reading.Token).ConfigureAwait(false);
            }

            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            if (tooLong)
            {
                await Send(writer, new IpcResponse(false, Error: "Request is too long."), _cancel.Token)
                    .ConfigureAwait(false);
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
                await Send(writer, new IpcResponse(false, Error: "Malformed request."), _cancel.Token)
                    .ConfigureAwait(false);
                return;
            }

            if (IpcProtocol.ClientRefusal(request.Protocol) is { } mismatch)
            {
                await Send(writer, new IpcResponse(false, Error: mismatch), _cancel.Token).ConfigureAwait(false);
                return;
            }

            if (IpcOps.ChecksRelease(request.Op) &&
                IpcProtocol.ReleaseRefusal(ProductVersion.Current.Text, request.Winshell) is { } release)
            {
                await Send(writer, new IpcResponse(false, Error: release), _cancel.Token).ConfigureAwait(false);
                return;
            }

            if (request.Op is IpcOps.Listen or IpcOps.Wait)
            {
                await Listen(request, writer, stream, request.Op == IpcOps.Wait).ConfigureAwait(false);
                return;
            }

            var response = await OnUiThread(() => Handle(request)).ConfigureAwait(false);
            try
            {
                await Send(writer, response, _cancel.Token).ConfigureAwait(false);
            }
            finally
            {
                if (request.Op == IpcOps.Reload && response.Ok)
                    _loop.Post(_control.Reloaded);
            }
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
            // Shutdown, or the request timeout, cancelled the pending read.
        }
    }

    /// <summary>Every request but a listen. A screenshot awaits WinUI, so inspect ends where the shell's work does.</summary>
    private async Task<IpcResponse> Handle(IpcRequest request)
    {
        var ipc = _control.Ipc;
        return request.Op switch
        {
            IpcOps.Show when request.Json => new IpcResponse(true, CliJson.IpcShow(ipc.Describe())),
            IpcOps.Show => new IpcResponse(true, ipc.Show()),
            IpcOps.Call when request is { Target: "notice", Name: "dismiss" } => DismissNotice(request.Args),
            IpcOps.Call => new IpcResponse(true,
                ipc.Call(request.Target ?? "", request.Name ?? "", request.Args ?? [])),
            IpcOps.Prop => new IpcResponse(true, ipc.GetProperty(request.Target ?? "", request.Name ?? "")),
            IpcOps.Kill => Kill(),
            IpcOps.Reload => Reload(request),
            IpcOps.Build => Build(request),
            IpcOps.Inspect => new IpcResponse(true,
                await _control.Inspect(request.Target ?? "", request.Args ?? [])),
            _ => new IpcResponse(false, Error: $"Unknown request {request.Op}.")
        };
    }

    /// <summary><c>winshell ipc call notice dismiss</c>. It takes no arguments and closes whichever notice is showing.</summary>
    private IpcResponse DismissNotice(List<string>? args)
    {
        if (args is { Count: > 0 })
            return new IpcResponse(false, Error: IpcValues.WrongCount("notice", "dismiss", 0, args.Count));
        _control.DismissNotice();
        return new IpcResponse(true);
    }

    private IpcResponse Kill()
    {
        _loop.Post(_control.Quit);
        return new IpcResponse(true);
    }

    private IpcResponse Reload(IpcRequest request)
    {
        return _control.Reload(request.ReloadProgram) is { } error
            ? new IpcResponse(false, Error: error)
            : new IpcResponse(true);
    }

    /// <summary>
    /// <c>winshell reload</c> reports a failed build with it, and <c>winshell dev</c> reports each build. An empty error
    /// says the project built, which closes that project's build-error notice.
    /// </summary>
    private IpcResponse Build(IpcRequest request)
    {
        if (request.BuildReport is not var (error, projectFile))
            return new IpcResponse(false, Error: "build takes the error and the project file.");
        _control.ShowBuildError(error, projectFile);
        return new IpcResponse(true);
    }

    private async Task Listen(IpcRequest request, StreamWriter writer, Stream stream, bool once)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueuedSignals)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        IDisposable? subscription = null;
        var subscribed = await OnUiThread(() =>
        {
            subscription = _control.Ipc.Listen(request.Target ?? "", request.Name ?? "",
                value => channel.Writer.TryWrite(value));
            return Task.FromResult(new IpcResponse(true));
        }).ConfigureAwait(false);
        if (!subscribed.Ok)
        {
            await Send(writer, subscribed, _cancel.Token).ConfigureAwait(false);
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
                var n = await stream.ReadAsync(bytes.AsMemory(count, MaxRequestBytes + 1 - count), cancel)
                    .ConfigureAwait(false);
                if (n == 0)
                    return (null, false);
                // A client sends one line, so nothing after the newline is lost. A listener sees a disconnect by a read of 0.
                var newline = Array.IndexOf(bytes, (byte)'\n', count, n);
                if (newline >= 0)
                {
                    if (newline > 0 && bytes[newline - 1] == (byte)'\r')
                        newline--;
                    return (Encoding.UTF8.GetString(bytes, 0, newline), false);
                }

                count += n;
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
        {
            _clients--;
        }
    }

    private void Track(NamedPipeServerStream stream)
    {
        lock (_limit)
        {
            _serving.Add(stream);
        }
    }

    private void Untrack(NamedPipeServerStream stream)
    {
        lock (_limit)
        {
            _serving.Remove(stream);
        }
    }

    /// <summary>
    /// Starts <paramref name="work"/> on the shell's thread. What it throws is a failed response: a script error is a
    /// failed request, not a crash report from an unobserved task.
    /// </summary>
    private Task<IpcResponse> OnUiThread(Func<Task<IpcResponse>> work)
    {
        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _loop.Post(() => _ = Run());
        return tcs.Task;

        async Task Run()
        {
            try
            {
                tcs.SetResult(await work());
            }
            catch (IpcException e)
            {
                tcs.SetResult(new IpcResponse(false, Error: e.Message));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                tcs.SetResult(new IpcResponse(false, Error: $"ipc: {e.GetType().Name}: {e.Message}"));
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        }
    }

    private static Task Send(StreamWriter writer, IpcResponse response, CancellationToken cancel)
    {
        return writer.WriteLineAsync(
            JsonSerializer
                .Serialize(response with { Protocol = IpcProtocol.Current, Winshell = ProductVersion.Current.Text },
                    IpcJson.Default.IpcResponse).AsMemory(), cancel);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancel.Cancel();
        NamedPipeServerStream[] open;
        lock (_limit)
        {
            open = [.. _serving];
        }

        foreach (var stream in open)
            try
            {
                stream.Dispose();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }

        lock (_firstGate)
        {
            _first?.Dispose();
            _first = null;
        }
    }
}
