using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell;

internal sealed record IpcRequest(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("target")] string? Target = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("args")] List<string>? Args = null);

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
    private readonly CancellationTokenSource _cancel = new();

    public IpcServer(string pipe, IEventLoop loop, Func<ShellHost> host, Action kill)
    {
        _pipe = pipe;
        _loop = loop;
        _host = host;
        _kill = kill;
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            // A cancelled wait, and a failed wait that this loop retries, used to drop the pipe without closing it.
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (IOException)
            {
                server?.Dispose();
                await Task.Delay(100).ConfigureAwait(false);
                continue;
            }
            if (server is null)
                continue;
            var connected = server;
            _ = Task.Run(() => Serve(connected));
        }
    }

    private async Task Serve(NamedPipeServerStream stream)
    {
        try
        {
            await using var _ = stream;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
                return;
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
                await Send(writer, new IpcResponse(false, Error: "Malformed request")).ConfigureAwait(false);
                return;
            }
            if (request.Op is "listen" or "wait")
            {
                await Listen(request, writer, once: request.Op == "wait").ConfigureAwait(false);
                return;
            }
            var response = await OnUiThread(() => Handle(request)).ConfigureAwait(false);
            await Send(writer, response).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The client closed the pipe (Ctrl+C, or it exited). That is not a crash.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private IpcResponse Handle(IpcRequest request)
    {
        var ipc = _host().Ipc;
        try
        {
            return request.Op switch
            {
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

    private async Task Listen(IpcRequest request, StreamWriter writer, bool once)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
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
            await Send(writer, new IpcResponse(false, Error: error)).ConfigureAwait(false);
            return;
        }
        try
        {
            await foreach (var value in channel.Reader.ReadAllAsync(_cancel.Token).ConfigureAwait(false))
            {
                await Send(writer, new IpcResponse(true, value)).ConfigureAwait(false);
                if (once)
                    break;
            }
        }
        catch (IOException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _loop.Post(() => subscription?.Dispose());
        }
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

    private static Task Send(StreamWriter writer, IpcResponse response) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcJson.Default.IpcResponse));

    public void Dispose() => _cancel.Cancel();

    /// <summary>Sends one request to an instance and returns its responses, one per line.</summary>
    public static async IAsyncEnumerable<IpcResponse> Request(string pipe, IpcRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel = default)
    {
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(3000, cancel).ConfigureAwait(false);
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
