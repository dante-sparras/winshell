using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Winshell.Winland;

/// <summary>
/// The one connection to Winland for the whole process: its event stream, and a fresh pipe connection per request.
/// While Winland is not running it looks for the pipe after 3 s, then twice as long each time, up to every 30 s. Its
/// events are raised on a background thread, in order; each engine's <see cref="WinlandService"/> posts them to its
/// own loop.
/// </summary>
internal static partial class WinlandConnection
{
    private static readonly TimeSpan FirstPoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LastPoll = TimeSpan.FromSeconds(30);
    private static readonly int SessionId = Process.GetCurrentProcess().SessionId;
    private static readonly IReadOnlyList<string> Candidates = WinlandProtocol.PipeNames(Environment.GetEnvironmentVariable("WINLAND_PIPE"), SessionId);
    private static readonly object LogsLock = new();
    private static readonly List<Log> Logs = [];
    private static int _started;
    private static volatile string? _pipeName;
    private static volatile bool _connected;

    /// <summary>An engine's log. A listener that throws is written there, not only to a debugger.</summary>
    internal static void RegisterLog(Log log)
    {
        lock (LogsLock)
        {
            if (!Logs.Contains(log))
                Logs.Add(log);
        }
    }

    /// <summary>Drops a log registered by <see cref="RegisterLog"/>.</summary>
    internal static void UnregisterLog(Log log)
    {
        lock (LogsLock)
            Logs.Remove(log);
    }

    /// <summary>Runs one listener the way an event does, so a test can see a thrown listener get logged.</summary>
    internal static void RaiseForTest(Action listener) => Raise<int>(_ => listener(), 0);

    /// <summary>Raised when the event stream connects (true) or is lost (false).</summary>
    public static event Action<bool>? ConnectionChanged;

    /// <summary>Raised for each line of the event stream.</summary>
    public static event Action<string>? EventReceived;

    public static bool IsConnected => _connected;

    /// <summary>The pipe Winland answers on, or the one it would use when it is not running.</summary>
    public static string PipePath => @"\\.\pipe\" + (_pipeName ?? Candidates[0]);

    public static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _ = Task.Run(FollowEvents);
    }

    private static async Task FollowEvents()
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
                try
                {
                    await pipe.WriteAsync(Encoding.UTF8.GetBytes("events\n")).ConfigureAwait(false);
                    await pipe.FlushAsync().ConfigureAwait(false);
                    _connected = true;
                    Raise(ConnectionChanged, true);
                    var reader = new WinlandReader(pipe);
                    while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                    {
                        if (line.Length > 0)
                            Raise(EventReceived, line);
                    }
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidDataException or InvalidOperationException)
                {
                }
                finally
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    if (_connected)
                    {
                        _connected = false;
                        Raise(ConnectionChanged, false);
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
    internal static TimeSpan NextPoll(TimeSpan poll) => poll * 2 < LastPoll ? poll * 2 : LastPoll;

    /// <summary>Sends one request and reads its reply. Null when Winland is not running or does not answer.</summary>
    public static async Task<string?> RequestAsync(string command)
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
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(command.ReplaceLineEndings(" ") + "\n"), timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                return await new WinlandReader(pipe).ReadReplyAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
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
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)
                || !ProcessIdToSessionId(pid, out var session) || session != SessionId)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                return null;
            }
            return pipe;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    private static void Raise<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception e)
            {
                var message = e.ToString();
                Log[] logs;
                lock (LogsLock)
                    logs = Logs.ToArray();
                if (logs.Length == 0)
                    Log.Default.Warn("winland", message);
                else
                    foreach (var log in logs)
                        log.Warn("winland", message);
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(nint pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);
}
