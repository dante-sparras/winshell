using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.DesktopEntries;

/// <summary>
/// Quickshell's <c>DesktopEntries</c>, from the Start menu: its shortcuts and its packaged apps. An entry's
/// <c>startupClass</c> is the AppUserModelID or program name a window of the app has as its <c>Toplevel.appId</c>, so
/// <c>heuristicLookup(toplevel.appId)</c> finds the app of a window.
/// </summary>
public sealed class DesktopEntries : QObject
{
    private readonly QmlEngine _engine;
    private readonly Dictionary<string, DesktopEntry> _byId = new(StringComparer.Ordinal);
    private IReadOnlyList<AppEntry> _entries = [];

    public DesktopEntries(QmlEngine engine)
    {
        _engine = engine;
        StartMenu.Changed += OnChanged;
        StartMenu.EnsureStarted();
        Sync();
    }

    /// <summary>The apps that are not hidden, by name.</summary>
    [Qml] public ObjectModel Applications { get; } = new();

    /// <summary>An entry by its id: a shortcut's name, or a packaged app's AppUserModelID. Includes hidden entries.</summary>
    [Qml]
    public DesktopEntry? ById(string id)
    {
        TrackQml("applications");
        var entry = _entries.FirstOrDefault(e => e.Id == id) ?? _entries.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : _byId.GetValueOrDefault(entry.Id);
    }

    [Qml]
    public DesktopEntry? HeuristicLookup(string name)
    {
        TrackQml("applications");
        return StartMenu.Lookup(_entries, name ?? "") is { } entry ? _byId.GetValueOrDefault(entry.Id) : null;
    }

    private void OnChanged() => _engine.Loop.Post(Sync);

    private void Sync()
    {
        if (IsDestroyed)
            return;
        _entries = StartMenu.Entries;
        var ids = _entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, gone) in _byId.Where(kv => !ids.Contains(kv.Key)).ToArray())
        {
            _byId.Remove(id);
            Applications.Remove(gone);
            gone.Destroy();
        }
        foreach (var entry in _entries)
        {
            if (!_byId.TryGetValue(entry.Id, out var obj))
                _byId[entry.Id] = obj = new DesktopEntry(entry.Id, _engine.Log);
            obj.Update(entry);
        }
        Applications.Set(_entries.Where(e => !e.NoDisplay).Select(e => (object?)_byId[e.Id]).ToList());
        NotifyChanged("applications");
    }

    protected override void OnDestroy()
    {
        StartMenu.Changed -= OnChanged;
        foreach (var entry in _byId.Values)
            entry.Destroy();
        _byId.Clear();
    }
}

/// <summary>Quickshell's <c>DesktopEntry</c>: one app of the Start menu.</summary>
public sealed class DesktopEntry : QObject
{
    private readonly Log _log;
    private AppEntry? _entry;
    private string _name = "", _genericName = "", _comment = "", _icon = "", _execString = "", _workingDirectory = "", _startupClass = "";
    private bool _noDisplay, _runInTerminal;
    private IReadOnlyList<string> _command = [], _categories = [];

    internal DesktopEntry(string id, Log log)
    {
        Id = id;
        _log = log;
    }

    [Qml] public string Id { get; }

    [Qml] public string Name { get { Track(); return _name; } set => SetField(ref _name, value ?? ""); }

    /// <summary>Windows has no generic names; empty.</summary>
    [Qml] public string GenericName { get { Track(); return _genericName; } set => SetField(ref _genericName, value ?? ""); }

    [Qml] public string Comment { get { Track(); return _comment; } set => SetField(ref _comment, value ?? ""); }

    /// <summary>A path the icon provider draws: the shortcut, or <c>shell:AppsFolder\&lt;id&gt;</c> for a packaged app.</summary>
    [Qml] public string Icon { get { Track(); return _icon; } set => SetField(ref _icon, value ?? ""); }

    [Qml] public string ExecString { get { Track(); return _execString; } set => SetField(ref _execString, value ?? ""); }

    [Qml] public IReadOnlyList<string> Command { get { Track(); return _command; } }

    [Qml] public string WorkingDirectory { get { Track(); return _workingDirectory; } set => SetField(ref _workingDirectory, value ?? ""); }

    /// <summary>The AppUserModelID the app's windows carry, or its program's name in lower case.</summary>
    [Qml] public string StartupClass { get { Track(); return _startupClass; } set => SetField(ref _startupClass, value ?? ""); }

    [Qml] public bool RunInTerminal { get { Track(); return _runInTerminal; } set => SetField(ref _runInTerminal, value); }

    /// <summary>The Start menu folders the shortcut is in.</summary>
    [Qml] public IReadOnlyList<string> Categories { get { Track(); return _categories; } }

    [Qml] public IReadOnlyList<string> Keywords => [];

    [Qml] public bool NoDisplay { get { Track(); return _noDisplay; } set => SetField(ref _noDisplay, value); }

    /// <summary>Start menu shortcuts have no actions.</summary>
    [Qml] public IReadOnlyList<DesktopAction> Actions => [];

    /// <summary>Opens the app as the Start menu does, through its shortcut or its AppUserModelID.</summary>
    [Qml]
    public void Execute()
    {
        if (_entry is { } entry)
            StartMenu.Launch(entry with { WorkingDirectory = _workingDirectory }, message => _log.Warn("desktopentry", message));
    }

    internal void Update(AppEntry entry)
    {
        _entry = entry;
        SetField(ref _name, entry.Name, nameof(Name));
        SetField(ref _comment, entry.Comment, nameof(Comment));
        SetField(ref _icon, entry.Icon, nameof(Icon));
        SetField(ref _execString, entry.ExecString, nameof(ExecString));
        SetField(ref _workingDirectory, entry.WorkingDirectory, nameof(WorkingDirectory));
        SetField(ref _startupClass, entry.StartupClass, nameof(StartupClass));
        SetField(ref _noDisplay, entry.NoDisplay, nameof(NoDisplay));
        if (!_command.SequenceEqual(entry.Command))
            SetField(ref _command, entry.Command, nameof(Command));
        if (!_categories.SequenceEqual(entry.Categories))
            SetField(ref _categories, entry.Categories, nameof(Categories));
    }

    public override string ToString() => $"DesktopEntry({Id})";
}

/// <summary>Quickshell's <c>DesktopAction</c>. Start menu shortcuts have none, so no entry lists one.</summary>
public sealed class DesktopAction : QObject
{
    [Qml] public string Id => "";

    [Qml] public string Name => "";

    [Qml] public string Icon => "";

    [Qml] public string ExecString => "";

    [Qml] public IReadOnlyList<string> Command => [];

    [Qml]
    public void Execute()
    {
    }
}
