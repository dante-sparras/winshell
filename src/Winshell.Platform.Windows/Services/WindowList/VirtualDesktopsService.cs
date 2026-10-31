using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.WindowList;

/// <summary>
/// The <c>VirtualDesktops</c> singleton: the desktops Task View shows. Listing, switching, creating and removing
/// use the internal manager only after a read of a known Windows build succeeds. Which desktop a window is on
/// uses the supported manager, including when this list is not supported.
/// </summary>
public sealed class VirtualDesktopsService : QObject
{
    private readonly QmlEngine? _engine;
    private readonly IVirtualDesktopSource _source;
    private readonly Log _log;
    private readonly bool _watch;
    private readonly Dictionary<string, VirtualDesktop> _byId = [];
    private readonly bool _contractSupported;
    private VirtualDesktop? _current;
    private bool _supported;
    private bool _proved;
    private string _reason = "";
    private string _stamp = "";
    private IDisposable? _reread;

    /// <summary>How often <see cref="IVirtualDesktopSource.ListStamp"/> is checked. Windows signals a switch, not an add or a remove.</summary>
    internal static readonly TimeSpan RereadInterval = TimeSpan.FromSeconds(1);

    public VirtualDesktopsService(QmlEngine engine)
        : this(engine, VirtualDesktopBroker.Shared, engine.Log, watch: true)
    {
    }

    internal VirtualDesktopsService(QmlEngine? engine, IVirtualDesktopSource source, Log log, bool watch)
    {
        _engine = engine;
        _source = source;
        _log = log;
        _watch = watch;
        Desktops = new ObjectModel();
        Desktops.SetOwner(this);
        Desktops.Engine = engine;
        _contractSupported = source.Contract.Supported;
        if (_watch && engine is not null && ReferenceEquals(source, VirtualDesktopBroker.Shared))
        {
            DesktopSwitchWatch.Ensure(engine.Loop, log);
            DesktopSwitchWatch.Fired += Refresh;
        }
        if (!_contractSupported)
            SetField(ref _reason, source.Contract.Reason, nameof(UnsupportedReason));
        else
            Refresh();
        if (_watch && engine is not null && _contractSupported)
        {
            _stamp = source.ListStamp;
            ScheduleRead();
        }
    }

    [Qml] public ObjectModel Desktops { get; }

    [Qml] public bool Supported { get { Track(); return _supported; } }

    /// <summary>Why <see cref="Supported"/> is false. Empty when the list can be used.</summary>
    [Qml] public string UnsupportedReason { get { Track(); return _reason; } }

    [Qml] public VirtualDesktop? Current { get { Track(); return _current; } }

    [Qml]
    public void SwitchTo(VirtualDesktop? desktop)
    {
        if (!CanChange("switch") || desktop is null || !Guid.TryParse(desktop.Id, out var id))
            return;
        if (!_source.SwitchTo(id))
        {
            _log.Warn("desktops", "The desktop did not change.");
            return;
        }
        Refresh();
    }

    [Qml]
    public VirtualDesktop? Create()
    {
        if (!CanChange("create a desktop"))
            return null;
        if (_source.Create() is not { } id)
        {
            _log.Warn("desktops", "The desktop was not created.");
            return null;
        }
        Refresh();
        return Find(VirtualDesktopRules.FormatId(id));
    }

    [Qml]
    public void Remove(VirtualDesktop? desktop)
    {
        if (!CanChange("remove a desktop") || desktop is null)
            return;
        var fallback = VirtualDesktopRules.FallbackIndex(desktop.Index, _byId.Count);
        if (fallback is null || DesktopsAt(fallback.Value) is not { } onto)
        {
            _log.Warn("desktops", "The last virtual desktop cannot be removed.");
            return;
        }
        if (!Guid.TryParse(desktop.Id, out var id) || !Guid.TryParse(onto.Id, out var fallbackId) || !_source.Remove(id, fallbackId))
        {
            _log.Warn("desktops", "The desktop was not removed.");
            return;
        }
        Refresh();
    }

    internal VirtualDesktop? Find(string id) => id.Length == 0 ? null : _byId.GetValueOrDefault(id);

    protected override void OnDestroy()
    {
        _reread?.Dispose();
        _reread = null;
        if (_watch)
            DesktopSwitchWatch.Fired -= Refresh;
    }

    private void ScheduleRead() => _reread = _engine!.Loop.Schedule(RereadInterval, ReadAgain);

    private void ReadAgain()
    {
        if (IsDestroyed)
            return;
        var stamp = _source.ListStamp;
        if (stamp != _stamp)
        {
            _stamp = stamp;
            Refresh();
        }
        if (!IsDestroyed)
            ScheduleRead();
    }

    private void Refresh()
    {
        if (IsDestroyed || !_contractSupported)
            return;
        if (!_source.TryRead(out var records, out var current, out var error))
        {
            _log.Warn("desktops", error);
            if (_proved)
                return;
            Clear();
            SetField(ref _supported, false, nameof(Supported));
            SetField(ref _reason, error, nameof(UnsupportedReason));
            return;
        }

        _proved = true;
        var currentId = VirtualDesktopRules.FormatId(current);
        var seen = new HashSet<string>();
        var ordered = new List<VirtualDesktop>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var id = VirtualDesktopRules.FormatId(records[i].Id);
            if (id.Length == 0 || !seen.Add(id))
                continue;
            if (!_byId.TryGetValue(id, out var desktop))
            {
                desktop = new VirtualDesktop(id) { Engine = _engine };
                desktop.SetOwner(this);
                _byId[id] = desktop;
            }
            desktop.Apply(VirtualDesktopRules.DisplayName(records[i].Name, ordered.Count), ordered.Count, id == currentId);
            ordered.Add(desktop);
        }

        foreach (var (id, desktop) in _byId.ToArray())
        {
            if (seen.Contains(id))
                continue;
            _byId.Remove(id);
            desktop.Destroy();
        }

        Desktops.Set(ordered.Cast<object?>().ToArray());
        SetField(ref _current, currentId.Length == 0 ? null : _byId.GetValueOrDefault(currentId), nameof(Current));
        SetField(ref _supported, true, nameof(Supported));
        SetField(ref _reason, "", nameof(UnsupportedReason));
        _engine?.FindNativeSingleton<WindowListService>()?.NotifyDesktopsChanged();
    }

    private void Clear()
    {
        foreach (var desktop in _byId.Values.ToArray())
            desktop.Destroy();
        _byId.Clear();
        Desktops.Clear();
        SetField(ref _current, null, nameof(Current));
    }

    private VirtualDesktop? DesktopsAt(int index)
    {
        foreach (var desktop in _byId.Values)
        {
            if (desktop.Index == index)
                return desktop;
        }
        return null;
    }

    private bool CanChange(string action)
    {
        if (_contractSupported && !_proved)
            Refresh();
        if (_proved && _contractSupported)
            return true;
        var why = _reason.Length == 0 ? "virtual desktops are not supported" : _reason;
        _log.Warn("desktops", $"Cannot {action}; {why}");
        return false;
    }
}

/// <summary>The <c>VirtualDesktop</c>: one desktop Task View can switch to.</summary>
public sealed class VirtualDesktop : QObject
{
    private string _name = "";
    private int _index;
    private bool _current;

    internal VirtualDesktop(string id) => Id = id;

    /// <summary>The desktop's id, the same text <c>AppWindow.desktopId</c> uses.</summary>
    [Qml] public string Id { get; }

    [Qml] public string Name { get { Track(); return _name; } }

    /// <summary>Where the desktop sits in <c>VirtualDesktops.desktops</c>, starting at zero.</summary>
    [Qml] public int Index { get { Track(); return _index; } }

    [Qml] public bool Current { get { Track(); return _current; } }

    internal void Apply(string name, int index, bool current)
    {
        SetField(ref _name, name, nameof(Name));
        SetField(ref _index, index, nameof(Index));
        SetField(ref _current, current, nameof(Current));
    }
}
