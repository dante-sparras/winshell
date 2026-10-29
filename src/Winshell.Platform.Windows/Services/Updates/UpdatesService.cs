using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Updates;

/// <summary>
/// The <c>Updates</c> singleton: Winshell's own updates. A portable Winshell from GitHub Releases checks its releases,
/// downloads a newer one and applies it when it next starts. A build from source and a packaged Winshell never update
/// themselves; <see cref="Available"/> is false for them.
/// </summary>
public sealed class UpdatesService : QObject
{
    private readonly Updater? _updater;
    private UpdateState _state = new(UpdateStatus.Idle, "", "");

    internal UpdatesService(QmlEngine engine)
    {
        _updater = engine.GetService<Updater>();
        if (_updater is null)
            return;
        _state = _updater.State;
        _updater.Changed += OnChanged;
    }

    /// <summary>Whether this Winshell updates itself.</summary>
    [Qml] public bool Available { get { Track(); return _updater is not null; } }

    /// <summary>
    /// True when Winshell runs from an MSIX package, such as the Microsoft Store's. The package's installer updates it.
    /// </summary>
    [Qml] public bool Packaged { get { Track(); return Packaging.IsPackaged; } }

    /// <summary>
    /// Whether Winshell checks by itself, shortly after it starts and once a day, and downloads what it finds. Off
    /// until the shell turns it on; <see cref="Check"/> works either way.
    /// </summary>
    [Qml]
    public bool Automatic
    {
        get { Track(); return _updater?.Automatic ?? false; }
        set
        {
            if (_updater is null || _updater.Automatic == value)
                return;
            _updater.Automatic = value;
            NotifyChanged("automatic");
        }
    }

    /// <summary>An <c>UpdateStatus</c>: <c>Idle</c>, <c>Checking</c>, <c>UpToDate</c>, <c>Downloading</c>, <c>Ready</c> or <c>Failed</c>.</summary>
    [Qml] public int Status { get { Track(); return (int)_state.Status; } }

    /// <summary>The release found: the newest one when up to date, the one downloading or ready. Empty before a check.</summary>
    [Qml] public string Version { get { Track(); return _state.Version; } }

    /// <summary>Why the last check, download or install failed, while <see cref="Status"/> is <c>Failed</c>.</summary>
    [Qml] public string Error { get { Track(); return _state.Error; } }

    /// <summary>Checks now, and downloads a newer release.</summary>
    [Qml]
    public void Check()
    {
        if (_updater is null)
        {
            ServiceSupport.Warn(this, "updates", "Updates.check(): this Winshell does not update itself.");
            return;
        }
        ServiceSupport.Run("updates", _updater.CheckAsync);
    }

    /// <summary>Starts Winshell over with the ready update. Does nothing while no update is <c>Ready</c>.</summary>
    [Qml]
    public void Restart()
    {
        if (_updater?.RestartToUpdate() != true)
            ServiceSupport.Warn(this, "updates", "Updates.restart(): no update is ready.");
    }

    private void OnChanged(UpdateState state)
    {
        void Apply()
        {
            if (IsDestroyed)
                return;
            var before = _state;
            _state = state;
            if (before.Status != state.Status) NotifyChanged("status");
            if (before.Version != state.Version) NotifyChanged("version");
            if (before.Error != state.Error) NotifyChanged("error");
        }
        if (Engine?.Loop is { } loop)
            loop.Post(Apply);
        else
            Apply();
    }

    protected override void OnDestroy()
    {
        if (_updater is not null)
            _updater.Changed -= OnChanged;
    }

    internal static void Register(ModuleRegistry registry) =>
        registry.Module("Winshell")
            .Singleton(e => new UpdatesService(e), "Updates")
            .Enum<UpdateStatus>(s => s.ToString());
}
