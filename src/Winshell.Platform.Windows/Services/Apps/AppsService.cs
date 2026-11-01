using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Apps;

/// <summary>
/// The <c>Apps</c> singleton in <c>Winshell.Apps</c>: the Start menu's shortcuts and packaged apps.
/// <c>find(window.appId)</c> finds the app of a window. <c>search</c> ranks a typed query.
/// </summary>
public sealed class AppsService : QObject
{
    private readonly QmlEngine _engine;
    private readonly AppCatalog _catalog;
    private readonly Dictionary<string, AppEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppUsage> _bumps = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AppRecord> _entries = [];
    private IReadOnlyDictionary<string, AppUsage> _usage = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);

    public AppsService(QmlEngine engine)
        : this(engine, AppCatalog.Shared)
    {
    }

    internal AppsService(QmlEngine engine, AppCatalog catalog)
    {
        _engine = engine;
        _catalog = catalog;
        Apps.Engine = Recent.Engine = Frequent.Engine = engine;
        _catalog.Changed += OnChanged;
        _catalog.Start();
        Sync();
    }

    /// <summary>The apps that are shown in Start, by name.</summary>
    [Qml] public ObjectModel Apps { get; } = new();

    /// <summary>Visible apps Windows has a last-used time for, newest first.</summary>
    [Qml] public ObjectModel Recent { get; } = new();

    /// <summary>Visible apps with a use count, most used first.</summary>
    [Qml] public ObjectModel Frequent { get; } = new();

    /// <summary>An entry by its id: a shortcut's path under Programs, or a packaged app's AppUserModelID. Includes hidden entries.</summary>
    [Qml]
    public AppEntry? ById(string id)
    {
        TrackQml("apps");
        if (string.IsNullOrEmpty(id))
            return null;
        return _byId.GetValueOrDefault(id);
    }

    /// <summary>Finds an app by id, then by the app id a window carries, then by display name. Includes hidden entries.</summary>
    [Qml]
    public AppEntry? Find(string name)
    {
        TrackQml("apps");
        var found = AppIndex.Find(_entries, name ?? "");
        return found is null ? null : _byId.GetValueOrDefault(found.Id);
    }

    /// <summary>Visible apps the query matches, best first. A blank query matches nothing.</summary>
    [Qml]
    public IReadOnlyList<AppEntry> Search(string query)
    {
        TrackQml("apps");
        var now = _engine.Time.GetUtcNow().ToUnixTimeMilliseconds();
        return AppIndex.Search(_entries, query ?? "", UsageFor, now)
            .Select(entry => _byId.GetValueOrDefault(entry.Id))
            .OfType<AppEntry>()
            .ToList();
    }

    private void OnChanged()
    {
        if (IsDestroyed)
            return;
        _engine.Loop.Post(() =>
        {
            if (!IsDestroyed)
                Sync();
        });
    }

    private void Sync()
    {
        if (IsDestroyed)
            return;
        var snapshot = _catalog.Snapshot;
        _entries = snapshot.Entries;
        _usage = snapshot.Usage;
        var ids = _entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, gone) in _byId.Where(pair => !ids.Contains(pair.Key)).ToArray())
        {
            _byId.Remove(id);
            _bumps.Remove(id);
            Apps.Remove(gone);
            Recent.Remove(gone);
            Frequent.Remove(gone);
            gone.Destroy();
        }
        foreach (var entry in _entries)
        {
            if (!_byId.TryGetValue(entry.Id, out var obj))
            {
                obj = new AppEntry(entry.Id, _engine.Log) { Engine = _engine };
                obj.Launched = OnLaunched;
                _byId[entry.Id] = obj;
            }
            obj.Update(entry, UsageFor(entry));
        }
        Publish();
    }

    private void OnLaunched(AppRecord entry)
    {
        var shown = UsageFor(entry);
        _bumps[entry.Id] = new AppUsage(shown.RunCount + 1, _engine.Time.GetUtcNow().ToUnixTimeMilliseconds());
        if (_byId.TryGetValue(entry.Id, out var obj))
            obj.Update(entry, UsageFor(entry));
        Publish();
    }

    private void Publish()
    {
        Apps.Set(_entries.Where(entry => !entry.Hidden).Select(entry => (object?)_byId[entry.Id]).ToList());
        Recent.Set(AppIndex.Recent(_entries, UsageFor).Select(entry => (object?)_byId[entry.Id]).ToList());
        Frequent.Set(AppIndex.Frequent(_entries, UsageFor).Select(entry => (object?)_byId[entry.Id]).ToList());
        NotifyChanged("apps");
        NotifyChanged("recent");
        NotifyChanged("frequent");
    }

    private AppUsage UsageFor(AppRecord entry)
    {
        var usage = AppUsage.Lookup(_usage, entry);
        if (_bumps.TryGetValue(entry.Id, out var bump))
            usage = AppUsage.Merge(usage, bump);
        return usage;
    }

    protected override void OnDestroy()
    {
        _catalog.Changed -= OnChanged;
        foreach (var entry in _byId.Values)
            entry.Destroy();
        _byId.Clear();
    }
}

/// <summary>The <c>AppEntry</c>: one app of the Start menu.</summary>
public sealed class AppEntry : QObject
{
    private readonly Log _log;
    private AppRecord? _entry;
    private string _name = "";
    private string _description = "";
    private string _icon = "";
    private string _target = "";
    private string _arguments = "";
    private string _commandLine = "";
    private string _startIn = "";
    private string _appId = "";
    private bool _hidden;
    private bool _packaged;
    private int _runCount;
    private double _lastUsed;
    private IReadOnlyList<string> _folders = [];

    internal AppEntry(string id, Log log)
    {
        Id = id;
        _log = log;
    }

    internal Action<AppRecord>? Launched { get; set; }

    /// <summary>A shortcut's path under Programs, such as <c>Accessories/Notepad</c>, or a packaged app's AppUserModelID.</summary>
    [Qml] public string Id { get; }

    [Qml] public string Name { get { Track(); return _name; } }

    /// <summary>The shortcut's comment, or the packaged app's description. May be empty.</summary>
    [Qml] public string Description { get { Track(); return _description; } }

    /// <summary>A path the icon provider draws: the shortcut, or <c>shell:AppsFolder\&lt;id&gt;</c> for a packaged app.</summary>
    [Qml] public string Icon { get { Track(); return _icon; } }

    /// <summary>The program the shortcut opens, or <c>shell:AppsFolder\&lt;id&gt;</c> for a packaged app.</summary>
    [Qml] public string Target { get { Track(); return _target; } }

    /// <summary>The shortcut's arguments. Empty for a packaged app.</summary>
    [Qml] public string Arguments { get { Track(); return _arguments; } }

    /// <summary>A command line that describes how the app is opened.</summary>
    [Qml] public string CommandLine { get { Track(); return _commandLine; } }

    /// <summary>The shortcut's Start in folder.</summary>
    [Qml] public string StartIn { get { Track(); return _startIn; } }

    /// <summary>The AppUserModelID the app's windows carry, or its program's name in lower case.</summary>
    [Qml] public string AppId { get { Track(); return _appId; } }

    /// <summary>The Start menu folders the shortcut is in. Empty for a packaged app.</summary>
    [Qml] public IReadOnlyList<string> Folders { get { Track(); return _folders; } }

    /// <summary>True for an uninstaller or a document shortcut. Hidden apps stay out of <c>apps</c>, <c>search</c>, <c>recent</c>, and <c>frequent</c>.</summary>
    [Qml] public bool Hidden { get { Track(); return _hidden; } }

    /// <summary>True when the app came from PackageManager.</summary>
    [Qml] public bool Packaged { get { Track(); return _packaged; } }

    /// <summary>How many times Windows, or <c>launch</c>, has run the app.</summary>
    [Qml] public int RunCount { get { Track(); return _runCount; } }

    /// <summary>When the app was last used, in Unix milliseconds, or 0 when Windows has no record.</summary>
    [Qml] public double LastUsed { get { Track(); return _lastUsed; } }

    /// <summary>
    /// Opens the app as the Start menu does, through its shortcut or its AppUserModelID. A packaged app starts in
    /// the background, so this returns before it is up.
    /// </summary>
    [Qml]
    public void Launch()
    {
        if (_entry is not { } entry || Engine is not { } engine)
            return;
        void Warn(string message) => _log.Warn("apps", $"Failed to launch {entry.Name}: {message}");
        if (entry.Packaged)
        {
            AppLaunch.Packaged(entry.Id, entry.Arguments, engine.Loop, Warn, () =>
            {
                if (!IsDestroyed)
                    Launched?.Invoke(entry);
            });
        }
        else if (AppLaunch.Shortcut(entry.LaunchPath, entry.StartIn, Warn))
        {
            Launched?.Invoke(entry);
        }
    }

    internal void Update(AppRecord entry, AppUsage usage)
    {
        _entry = entry;
        SetField(ref _name, entry.Name, nameof(Name));
        SetField(ref _description, entry.Description, nameof(Description));
        SetField(ref _icon, entry.Icon, nameof(Icon));
        SetField(ref _target, entry.Target, nameof(Target));
        SetField(ref _arguments, entry.Arguments, nameof(Arguments));
        SetField(ref _commandLine, entry.CommandLine, nameof(CommandLine));
        SetField(ref _startIn, entry.StartIn, nameof(StartIn));
        SetField(ref _appId, entry.AppId, nameof(AppId));
        SetField(ref _hidden, entry.Hidden, nameof(Hidden));
        SetField(ref _packaged, entry.Packaged, nameof(Packaged));
        SetField(ref _runCount, usage.RunCount, nameof(RunCount));
        SetField(ref _lastUsed, usage.LastUsedUnixMilliseconds, nameof(LastUsed));
        if (!_folders.SequenceEqual(entry.Folders))
            SetField(ref _folders, entry.Folders, nameof(Folders));
    }

    public override string ToString() => $"AppEntry({Id})";
}
