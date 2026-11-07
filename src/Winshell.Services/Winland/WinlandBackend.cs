using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Winshell.Native;

namespace Winshell.Winland;

/// <summary>
/// The one connection to Winland for the whole process, <see cref="Instance"/>: its event stream, and a fresh pipe
/// connection per request. While Winland is not running it looks for the pipe after 3 s, then twice as long each time,
/// up to every 30 s. Its events are raised on a background thread, in order; each engine's
/// <see cref="WinlandService"/> posts them to its own loop.
/// </summary>
internal sealed class WinlandBackend : IWinlandBackend
{
    private static readonly TimeSpan FirstPoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LastPoll = TimeSpan.FromSeconds(30);
    private static readonly int SessionId = Process.GetCurrentProcess().SessionId;

    private static readonly IReadOnlyList<string> Candidates =
        WinlandProtocol.PipeNames(Environment.GetEnvironmentVariable("WINLAND_PIPE"), SessionId);

    public static readonly WinlandBackend Instance = new();

    private readonly Listeners<Handlers> _listeners = new();
    private int _started;
    private volatile string? _pipeName;
    private volatile int _processId;
    private volatile bool _connected;

    public bool IsConnected => _connected;

    public int ProcessId => _processId;

    public string PipePath => @"\\.\pipe\" + (_pipeName ?? Candidates[0]);

    public IDisposable Listen(Action<bool> connectionChanged, Action<string> eventReceived, Action<Exception> failed)
    {
        var listening = _listeners.Add(new Handlers(connectionChanged, eventReceived), failed);
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _ = Task.Run(FollowEvents);
        return listening;
    }

    private async Task FollowEvents()
    {
        var poll = FirstPoll;
        while (true)
        {
            var lost = false;
            foreach (var name in Candidates)
            {
                var pipe = await ConnectAsync(name, 250).ConfigureAwait(false);
                if (pipe is null)
                    continue;
                _pipeName = name;
                _processId = ServerProcess(pipe) is { } pid ? (int)pid : 0;
                try
                {
                    await pipe.WriteAsync(Encoding.UTF8.GetBytes("events\n")).ConfigureAwait(false);
                    await pipe.FlushAsync().ConfigureAwait(false);
                    _connected = true;
                    _listeners.Raise(static handlers => handlers.ConnectionChanged(true));
                    var reader = new WinlandReader(pipe);
                    while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                        if (line.Length > 0)
                            _listeners.Raise(handlers => handlers.EventReceived(line));
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidDataException
                                              or InvalidOperationException)
                {
                }
                finally
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    _processId = 0;
                    if (_connected)
                    {
                        _connected = false;
                        _listeners.Raise(static handlers => handlers.ConnectionChanged(false));
                    }
                }

                lost = true;
                break;
            }

            // Winland drops a subscriber that falls behind, so a lost stream is tried again at once, then from 3 s.
            var wait = lost ? TimeSpan.FromMilliseconds(250) : poll;
            poll = lost ? FirstPoll : NextPoll(poll);
            await Task.Delay(wait).ConfigureAwait(false);
        }
    }

    /// <summary>The wait after <paramref name="poll"/> finds no Winland: twice as long, at most 30 s.</summary>
    internal static TimeSpan NextPoll(TimeSpan poll)
    {
        return poll * 2 < LastPoll ? poll * 2 : LastPoll;
    }

    public async Task<string?> RequestAsync(string command)
    {
        var names = _pipeName is { } known ? [known] : Candidates;
        foreach (var name in names)
        {
            await using var pipe = await ConnectAsync(name, 1000).ConfigureAwait(false);
            if (pipe is null)
                continue;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(command.ReplaceLineEndings(" ") + "\n"), timeout.Token)
                    .ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                return await new WinlandReader(pipe).ReadReplyAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException
                                          or InvalidDataException)
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync(string name, int timeoutMs)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMs).ConfigureAwait(false);
            // Only a Winland in this session is ours to talk to, as Winland's own CLI checks.
            if (ServerProcess(pipe) is not { } pid || ProcessSession.Of(pid) is not { } session || session != SessionId)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return pipe;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException
                                      or OperationCanceledException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    private static uint? ServerProcess(NamedPipeClientStream pipe)
    {
        return Win32.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) ? pid : null;
    }

    private sealed record Handlers(Action<bool> ConnectionChanged, Action<string> EventReceived);
}
