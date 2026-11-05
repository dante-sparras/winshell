using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Winshell;

/// <summary>The side of the control pipe that sends a request to a running instance.</summary>
internal static class IpcClient
{
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
        // Written straight to the pipe, with no writer to flush on the way out: a shell that closed its end without
        // answering would make that flush throw, and the instance would look unreachable.
        var sent = JsonSerializer.Serialize(request with { Protocol = IpcProtocol.Current, Winshell = ProductVersion.Current.Text }, IpcJson.Default.IpcRequest);
        await client.WriteAsync(Encoding.UTF8.GetBytes(sent + Environment.NewLine), cancel).ConfigureAwait(false);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancel).ConfigureAwait(false);
            if (line is null)
                yield break;
            var response = JsonSerializer.Deserialize(line, IpcJson.Default.IpcResponse);
            if (response is null)
                continue;
            // A failed response is already a sentence for the user, whichever side wrote it.
            if (response.Ok && IpcProtocol.ShellRefusal(response.Protocol) is { } mismatch)
                throw new IOException(mismatch);
            if (response.Ok && IpcOps.ChecksRelease(request.Op) && IpcProtocol.ReleaseRefusal(response.Winshell, ProductVersion.Current.Text) is { } release)
                throw new IOException(release);
            yield return response;
        }
    }
}
