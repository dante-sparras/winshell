using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Winshell;

/// <summary>Why a request has no answer from the shell.</summary>
/// <param name="Reason">The sentence that says why, without the instance.</param>
/// <param name="Rejected">
/// True when this winshell rejected the shell: its owner, its integrity, its protocol or its release. False when the
/// pipe could not be reached or the connection timed out, and <paramref name="Reason"/> is that failure's message.
/// </param>
internal readonly record struct IpcFailure(string Reason, bool Rejected)
{
    /// <summary>The sentence for the user, naming the instance. A rejection is not reported as unreachable.</summary>
    public string Sentence(string instanceId)
    {
        return Rejected
            ? $"Rejected instance {instanceId}: {Reason}"
            : $"Could not reach instance {instanceId}: {Reason}";
    }
}

/// <summary>One answer from a shell, or the reason this winshell has none. A shell that refuses is an answer.</summary>
internal readonly record struct IpcReply(IpcResponse? Response, IpcFailure? Failure)
{
    public static IpcReply Answer(IpcResponse response)
    {
        return new IpcReply(response, null);
    }

    public static IpcReply Miss(IpcFailure failure)
    {
        return new IpcReply(null, failure);
    }
}

/// <summary>The side of the control pipe that sends a request to a running instance.</summary>
internal static class IpcClient
{
    /// <summary>
    /// Sends one request and returns each answer. A pipe that cannot be reached, a connection that times out, and a
    /// shell this winshell rejects come back as one reply with a failure, and then the stream ends. Cancelling
    /// <paramref name="cancel"/> is not a failure.
    /// </summary>
    public static async IAsyncEnumerable<IpcReply> Request(string pipe, IpcRequest request,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var connected = await Connect(pipe, cancel).ConfigureAwait(false);
        if (connected.Failure is { } connectFailure)
        {
            yield return IpcReply.Miss(connectFailure);
            yield break;
        }

        // Not CurrentUserOnly: that compares the pipe's owner with the token's default owner, which differs when
        // one side is elevated. ServerRefusal checks the owner against the user instead.
        await using var client = connected.Client!;
        if (PipeTrust.ServerRefusal(client) is { } refusal)
        {
            yield return IpcReply.Miss(new IpcFailure(refusal, true));
            yield break;
        }

        using var reader = new StreamReader(client, Encoding.UTF8);
        // Written straight to the pipe, with no writer to flush on the way out: a shell that closed its end without
        // answering would make that flush throw, and the instance would look unreachable.
        var sent = JsonSerializer.Serialize(
            request with { Protocol = IpcProtocol.Current, Winshell = ProductVersion.Current.Text },
            IpcJson.Default.IpcRequest);
        if (await Write(client, sent, cancel).ConfigureAwait(false) is { } writeFailure)
        {
            yield return IpcReply.Miss(writeFailure);
            yield break;
        }

        while (true)
        {
            var read = await Read(reader, cancel).ConfigureAwait(false);
            if (read.Failure is { } readFailure)
            {
                yield return IpcReply.Miss(readFailure);
                yield break;
            }

            if (read.Line is null)
                yield break;
            var response = JsonSerializer.Deserialize(read.Line, IpcJson.Default.IpcResponse);
            if (response is null)
                continue;
            // A failed response is already a sentence for the user, whichever side wrote it.
            if (response.Ok && IpcProtocol.ShellRefusal(response.Protocol) is { } mismatch)
            {
                yield return IpcReply.Miss(new IpcFailure(mismatch, true));
                yield break;
            }

            if (response.Ok && IpcOps.ChecksRelease(request.Op) &&
                IpcProtocol.ReleaseRefusal(response.Winshell, ProductVersion.Current.Text) is { } release)
            {
                yield return IpcReply.Miss(new IpcFailure(release, true));
                yield break;
            }

            yield return IpcReply.Answer(response);
        }
    }

    private readonly record struct Connection(NamedPipeClientStream? Client, IpcFailure? Failure);

    private static async Task<Connection> Connect(string pipe, CancellationToken cancel)
    {
        var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = false;
        try
        {
            await client.ConnectAsync(3000, cancel).ConfigureAwait(false);
            connected = true;
            return new Connection(client, null);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new Connection(null, new IpcFailure(e.Message, false));
        }
        finally
        {
            if (!connected)
                await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<IpcFailure?> Write(NamedPipeClientStream client, string json, CancellationToken cancel)
    {
        try
        {
            await client.WriteAsync(Encoding.UTF8.GetBytes(json + Environment.NewLine), cancel).ConfigureAwait(false);
            return null;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new IpcFailure(e.Message, false);
        }
    }

    private readonly record struct ReadLine(string? Line, IpcFailure? Failure);

    private static async Task<ReadLine> Read(StreamReader reader, CancellationToken cancel)
    {
        try
        {
            return new ReadLine(await reader.ReadLineAsync(cancel).ConfigureAwait(false), null);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return new ReadLine(null, new IpcFailure(e.Message, false));
        }
    }
}
