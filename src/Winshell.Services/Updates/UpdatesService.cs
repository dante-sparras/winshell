namespace Winshell.Updates;

/// <summary>
/// Winshell's own updates. A portable Winshell from GitHub Releases checks its releases, downloads a newer one and
/// applies it when it next starts. A build from source, a packaged Winshell and a winget install never update
/// themselves; <see cref="Available"/> is false for them.
/// </summary>
public sealed class UpdatesService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly Updater? _updater;
    private UpdateState _state = new(UpdateStatus.Idle, "", "");
    private bool _disposed;

    /// <param name="loop">The loop changes are raised on.</param>
    /// <param name="log">Where a request that cannot run is reported.</param>
    /// <param name="updater">The process's updater, or null when this Winshell does not update itself.</param>
    internal UpdatesService(IEventLoop loop, Log log, Updater? updater)
    {
        _loop = loop;
        _log = log;
        _updater = updater;
        if (_updater is null)
            return;
        _updater.Changed += OnChanged;
        _state = _updater.State;
    }

    /// <summary>Whether this Winshell updates itself.</summary>
    public bool Available => _updater is not null;

    /// <summary>
    /// True when Winshell runs from an MSIX package, such as the Microsoft Store's. The package's installer updates it.
    /// </summary>
    public bool Packaged => Packaging.IsPackaged;

    /// <summary>
    /// True when this copy lives under winget's Packages or Links folder. winget updates it, and Winshell does not.
    /// </summary>
    public bool Winget => Updater.IsWingetManaged(AppContext.BaseDirectory);

    /// <summary>
    /// Whether Winshell checks by itself, shortly after it starts and once a day, and downloads what it finds. Off
    /// until the shell turns it on; <see cref="Check"/> works either way.
    /// </summary>
    public bool Automatic
    {
        get => _updater?.Automatic ?? false;
        set
        {
            if (_updater is null || _updater.Automatic == value)
                return;
            _updater.Automatic = value;
            Changed();
        }
    }

    public UpdateStatus Status => _state.Status;

    /// <summary>The release found: the newest one when up to date, the one downloading or ready. Empty before a check.</summary>
    public string Version => _state.Version;

    /// <summary>
    /// Why the last check, download or install failed, while <see cref="Status"/> is <c>Failed</c>; or, while it is
    /// <c>Ready</c>, why <see cref="Restart"/> did not restart.
    /// </summary>
    public string Error => _state.Error;

    /// <summary>Checks now, and downloads a newer release.</summary>
    public void Check()
    {
        if (_updater is null)
        {
            _log.Warn("updates", "This Winshell does not update itself.");
            return;
        }
        ServiceSupport.Run("updates", _updater.CheckAsync);
    }

    /// <summary>Starts Winshell over with the ready update. Does nothing while no update is <c>Ready</c>.</summary>
    public void Restart()
    {
        if (_updater?.RestartToUpdate() != true)
            _log.Warn("updates", "No update is ready.");
    }

    private void OnChanged(UpdateState state) => _loop.Post(() => Apply(state));

    private void Apply(UpdateState state)
    {
        if (_disposed)
            return;
        var old = _state;
        _state = state;
        Changed(old.Status != state.Status, nameof(Status));
        Changed(old.Version != state.Version, nameof(Version));
        Changed(old.Error != state.Error, nameof(Error));
    }

    public void Dispose()
    {
        _disposed = true;
        if (_updater is not null)
            _updater.Changed -= OnChanged;
    }
}
