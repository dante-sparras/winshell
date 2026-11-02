namespace Winshell.Winland;

/// <summary>What the service needs of Winland. A test gives it a fake, and no pipe is opened.</summary>
internal interface IWinlandLink : IDisposable
{
    bool IsConnected { get; }

    string PipePath { get; }

    event Action<bool>? ConnectionChanged;

    event Action<string>? EventReceived;

    void Start();

    Task<string?> RequestAsync(string command);
}

/// <summary>The process-wide <see cref="WinlandConnection"/>, as one service sees it.</summary>
internal sealed class WinlandPipe(Log log) : IWinlandLink
{
    public bool IsConnected => WinlandConnection.IsConnected;

    public string PipePath => WinlandConnection.PipePath;

    public event Action<bool>? ConnectionChanged
    {
        add => WinlandConnection.ConnectionChanged += value;
        remove => WinlandConnection.ConnectionChanged -= value;
    }

    public event Action<string>? EventReceived
    {
        add => WinlandConnection.EventReceived += value;
        remove => WinlandConnection.EventReceived -= value;
    }

    public void Start()
    {
        WinlandConnection.RegisterLog(log);
        WinlandConnection.EnsureStarted();
    }

    public Task<string?> RequestAsync(string command) => WinlandConnection.RequestAsync(command);

    public void Dispose() => WinlandConnection.UnregisterLog(log);
}
