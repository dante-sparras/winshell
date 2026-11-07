namespace Winshell.Winland;

/// <summary>Winland's pipe, as <see cref="WinlandService"/> uses it: the event stream and one request at a time.</summary>
internal interface IWinlandBackend
{
    /// <summary>Whether the event stream is up.</summary>
    bool IsConnected { get; }

    /// <summary>The pipe Winland answers on, or the one it would use when it is not running.</summary>
    string PipePath { get; }

    /// <summary>The process that answers on the pipe, or 0 while none does.</summary>
    int ProcessId { get; }

    /// <summary>
    /// Follows the event stream, when it does not already, and calls the handlers until disposed. They run on any
    /// thread.
    /// </summary>
    /// <param name="connectionChanged">Gets true when the event stream connects, and false when it is lost.</param>
    /// <param name="eventReceived">Gets each line of the event stream, in order.</param>
    /// <param name="failed">Gets each exception a handler throws.</param>
    IDisposable Listen(Action<bool> connectionChanged, Action<string> eventReceived, Action<Exception> failed);

    /// <summary>Sends one request and reads its reply. Null when Winland is not running or does not answer.</summary>
    Task<string?> RequestAsync(string command);
}
