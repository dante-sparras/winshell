using System.Collections.ObjectModel;

namespace Winshell.Apps;

/// <summary>
/// The Start menu's shortcuts and packaged apps. <see cref="Find"/> finds the app of a window, and
/// <see cref="Search"/> ranks a typed query.
/// </summary>
public sealed class AppsService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly AppCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, AppEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppUsage> _bumps = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AppRecord> _entries = [];
    private IReadOnlyDictionary<string, AppUsage> _usage = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal AppsService(IEventLoop loop, Log log, AppCatalog? catalog = null, TimeProvider? time = null)
    {
        _loop = loop;
        _log = log;
        _catalog = catalog ?? AppCatalog.Shared;
        _time = time ?? TimeProvider.System;
        _catalog.Changed += OnChanged;
        _catalog.Start();
        Apply();
    }

    /// <summary>The apps that are shown in Start, by name.</summary>
    public ObservableCollection<AppEntry> Apps { get; } = [];

    /// <summary>Visible apps Windows has a last-used time for, newest first.</summary>
    public ObservableCollection<AppEntry> Recent { get; } = [];

    /// <summary>Visible apps with a use count, most used first.</summary>
    public ObservableCollection<AppEntry> Frequent { get; } = [];

    /// <summary>An entry by its id: a shortcut's path under Programs, or a packaged app's AppUserModelID. Includes hidden entries.</summary>
    public AppEntry? ById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Finds an app by id, then by the app id a window carries, then by display name. Includes hidden entries.</summary>
    public AppEntry? Find(string name) =>
        AppIndex.Find(_entries, name) is { } found ? _byId[found.Id] : null;

    /// <summary>Visible apps the query matches, best first. A blank query matches nothing.</summary>
    public IReadOnlyList<AppEntry> Search(string query) =>
        AppIndex.Search(_entries, query, UsageFor, _time.GetUtcNow().ToUnixTimeMilliseconds()).Select(entry => _byId[entry.Id]).ToList();

    private void OnChanged() => _loop.Post(Apply);

    private void Apply()
    {
        if (_disposed)
            return;
        var snapshot = _catalog.Snapshot;
        _entries = snapshot.Entries;
        _usage = snapshot.Usage;
        var ids = _entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, gone) in _byId.Where(pair => !ids.Contains(pair.Key)).ToArray())
        {
            _byId.Remove(id);
            _bumps.Remove(id);
            gone.Launched = null;
        }
        foreach (var entry in _entries)
        {
            if (_byId.TryGetValue(entry.Id, out var kept))
                kept.Update(entry, UsageFor(entry));
            else
                _byId[entry.Id] = new AppEntry(entry, UsageFor(entry), _loop, _log) { Launched = OnLaunched };
        }
        Publish();
    }

    private void OnLaunched(AppRecord entry)
    {
        if (_disposed)
            return;
        var shown = UsageFor(entry);
        _bumps[entry.Id] = new AppUsage(shown.RunCount + 1, _time.GetUtcNow().ToUnixTimeMilliseconds());
        if (_byId.TryGetValue(entry.Id, out var obj))
            obj.Update(entry, UsageFor(entry));
        Publish();
    }

    private void Publish()
    {
        Show(Apps, _entries.Where(entry => !entry.Hidden));
        Show(Recent, AppIndex.Recent(_entries, UsageFor));
        Show(Frequent, AppIndex.Frequent(_entries, UsageFor));
    }

    private void Show(ObservableCollection<AppEntry> list, IEnumerable<AppRecord> records) =>
        list.Sync(records.Select(entry => _byId[entry.Id]), entry => entry, entry => entry, entry => entry, static (_, _) => { });

    private AppUsage UsageFor(AppRecord entry)
    {
        var usage = AppUsage.Lookup(_usage, entry);
        if (_bumps.TryGetValue(entry.Id, out var bump))
            usage = AppUsage.Merge(usage, bump);
        return usage;
    }

    public void Dispose()
    {
        _disposed = true;
        _catalog.Changed -= OnChanged;
    }
}

/// <summary>One app of the Start menu.</summary>
public sealed class AppEntry : ObservableObject
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private AppRecord _entry;
    private AppUsage _usage;

    internal AppEntry(AppRecord entry, AppUsage usage, IEventLoop loop, Log log)
    {
        Id = entry.Id;
        _entry = entry;
        _usage = usage;
        _loop = loop;
        _log = log;
    }

    internal Action<AppRecord>? Launched { get; set; }

    /// <summary>A shortcut's path under Programs, such as <c>Accessories/Notepad</c>, or a packaged app's AppUserModelID.</summary>
    public string Id { get; }

    public string Name => _entry.Name;

    /// <summary>The shortcut's comment, or the packaged app's description. May be empty.</summary>
    public string Description => _entry.Description;

    /// <summary>A path an icon is read from: the shortcut, or <c>shell:AppsFolder\&lt;id&gt;</c> for a packaged app.</summary>
    public string Icon => _entry.Icon;

    /// <summary>The program the shortcut opens, or <c>shell:AppsFolder\&lt;id&gt;</c> for a packaged app.</summary>
    public string Target => _entry.Target;

    /// <summary>The shortcut's arguments. Empty for a packaged app.</summary>
    public string Arguments => _entry.Arguments;

    /// <summary>A command line that describes how the app is opened.</summary>
    public string CommandLine => _entry.CommandLine;

    /// <summary>The shortcut's Start in folder.</summary>
    public string StartIn => _entry.StartIn;

    /// <summary>The AppUserModelID the app's windows carry, or its program's name in lower case.</summary>
    public string AppId => _entry.AppId;

    /// <summary>The Start menu folders the shortcut is in. Empty for a packaged app.</summary>
    public IReadOnlyList<string> Folders => _entry.Folders;

    /// <summary>True for an uninstaller or a document shortcut. Hidden apps stay out of <c>Apps</c>, <c>Search</c>, <c>Recent</c> and <c>Frequent</c>.</summary>
    public bool Hidden => _entry.Hidden;

    /// <summary>True when the app came from PackageManager.</summary>
    public bool Packaged => _entry.Packaged;

    /// <summary>How many times Windows, or <see cref="Launch"/>, has run the app.</summary>
    public int RunCount => _usage.RunCount;

    /// <summary>When the app was last used, in Unix milliseconds, or 0 when Windows has no record.</summary>
    public long LastUsed => _usage.LastUsedUnixMilliseconds;

    /// <summary>
    /// Opens the app as the Start menu does, through its shortcut or its AppUserModelID. A packaged app starts in
    /// the background, so this returns before it is up.
    /// </summary>
    public void Launch()
    {
        var entry = _entry;
        void Warn(string message) => _log.Warn("apps", $"Failed to launch {entry.Name}: {message}");
        if (entry.Packaged)
            AppLaunch.Packaged(entry.Id, entry.Arguments, _loop, Warn, () => Launched?.Invoke(entry));
        else if (AppLaunch.Shortcut(entry.LaunchPath, entry.StartIn, Warn))
            Launched?.Invoke(entry);
    }

    internal void Update(AppRecord entry, AppUsage usage)
    {
        var old = _entry;
        var oldUsage = _usage;
        _entry = entry;
        _usage = usage;
        Changed(old.Name != entry.Name, nameof(Name));
        Changed(old.Description != entry.Description, nameof(Description));
        Changed(old.Icon != entry.Icon, nameof(Icon));
        Changed(old.Target != entry.Target, nameof(Target));
        Changed(old.Arguments != entry.Arguments, nameof(Arguments));
        Changed(old.CommandLine != entry.CommandLine, nameof(CommandLine));
        Changed(old.StartIn != entry.StartIn, nameof(StartIn));
        Changed(old.AppId != entry.AppId, nameof(AppId));
        Changed(!old.Folders.SequenceEqual(entry.Folders), nameof(Folders));
        Changed(old.Hidden != entry.Hidden, nameof(Hidden));
        Changed(old.Packaged != entry.Packaged, nameof(Packaged));
        Changed(oldUsage.RunCount != usage.RunCount, nameof(RunCount));
        Changed(oldUsage.LastUsedUnixMilliseconds != usage.LastUsedUnixMilliseconds, nameof(LastUsed));
    }

    public override string ToString() => $"AppEntry({Id})";
}
