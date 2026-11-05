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

    /// <summary>Raised when the event stream connects (true) or is lost (false). Raised on any thread.</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>Raised for each line of the event stream, in order. Raised on any thread.</summary>
    event Action<string>? EventReceived;

    /// <summary>Follows the event stream, when it does not already, and writes a listener that throws to <paramref name="log"/>.</summary>
    void Start(Log log);

    /// <summary>Stops writing to <paramref name="log"/>. The stream goes on for other services.</summary>
    void Stop(Log log);

    /// <summary>Sends one request and reads its reply. Null when Winland is not running or does not answer.</summary>
    Task<string?> RequestAsync(string command);
}
