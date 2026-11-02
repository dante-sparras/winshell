using System.Collections.ObjectModel;

namespace Winshell.Windows;

/// <summary>
/// The desktops Task View shows. Listing, switching, creating and removing use the internal manager only after a
/// read of a known Windows build succeeds. Which desktop a window is on uses the supported manager, including when
/// this list is not supported.
/// </summary>
public sealed class VirtualDesktopsService : ObservableObject, IDisposable
{
    /// <summary>How often <see cref="IVirtualDesktopSource.ListStamp"/> is checked. Windows signals a switch, not an add or a remove.</summary>
    internal static readonly TimeSpan RereadInterval = TimeSpan.FromSeconds(1);

    private readonly IEventLoop _loop;
    private readonly IVirtualDesktopSource _source;
    private readonly Log _log;
    private readonly bool _contractSupported;
    private VirtualDesktop? _current;
    private bool _supported;
    private bool _proved;
    private bool _disposed;
    private string _reason = "";
    private string _stamp = "";
    private IDisposable? _reread;

    internal VirtualDesktopsService(IEventLoop loop, Log log, IVirtualDesktopSource? source = null)
    {
        _loop = loop;
        _log = log;
        _source = source ?? VirtualDesktopBroker.Shared;
        _contractSupported = _source.Contract.Supported;
        if (!_contractSupported)
        {
            _reason = _source.Contract.Reason;
            return;
        }
        if (source is null)
        {
            DesktopSwitchWatch.Ensure(loop, log);
            DesktopSwitchWatch.Fired += OnSwitched;
        }
        Refresh();
        _stamp = _source.ListStamp;
        ScheduleRead();
    }

    public ObservableCollection<VirtualDesktop> Desktops { get; } = [];

    public bool Supported => _supported;

    /// <summary>Why <see cref="Supported"/> is false. Empty when the list can be used.</summary>
    public string UnsupportedReason => _reason;

    public VirtualDesktop? Current => _current;

    /// <summary>Raised after each read of the list, so windows can say which desktop they are on.</summary>
    internal event Action? Refreshed;

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

    public void Remove(VirtualDesktop? desktop)
    {
        if (!CanChange("remove a desktop") || desktop is null)
            return;
        if (VirtualDesktopRules.FallbackIndex(desktop.Index, Desktops.Count) is not { } fallback)
        {
            _log.Warn("desktops", "The last virtual desktop cannot be removed.");
            return;
        }
        if (!Guid.TryParse(desktop.Id, out var id) || !Guid.TryParse(Desktops[fallback].Id, out var fallbackId) || !_source.Remove(id, fallbackId))
        {
            _log.Warn("desktops", "The desktop was not removed.");
            return;
        }
        Refresh();
    }

    internal VirtualDesktop? Find(string id) => Desktops.FirstOrDefault(desktop => desktop.Id == id);

    private void OnSwitched() => _loop.Post(Refresh);

    private void ScheduleRead() => _reread = _loop.Schedule(RereadInterval, ReadAgain);

    private void ReadAgain()
    {
        if (_disposed)
            return;
        var stamp = _source.ListStamp;
        if (stamp != _stamp)
        {
            _stamp = stamp;
            Refresh();
        }
        ScheduleRead();
    }

    private void Refresh()
    {
        if (_disposed || !_contractSupported)
            return;
        if (!_source.TryRead(out var records, out var current, out var error))
        {
            _log.Warn("desktops", error);
            if (_proved)
                return;
            Desktops.Clear();
            Set(ref _current, null, nameof(Current));
            Set(ref _supported, false, nameof(Supported));
            Set(ref _reason, error, nameof(UnsupportedReason));
            return;
        }

        _proved = true;
        var currentId = VirtualDesktopRules.FormatId(current);
        var rows = new List<DesktopRow>(records.Count);
        foreach (var record in records)
        {
            var id = VirtualDesktopRules.FormatId(record.Id);
            if (id.Length > 0 && !rows.Exists(row => row.Id == id))
                rows.Add(new DesktopRow(id, VirtualDesktopRules.DisplayName(record.Name, rows.Count), rows.Count));
        }
        Desktops.Sync(rows, desktop => desktop.Id, row => row.Id, row => new VirtualDesktop(row.Id),
            (desktop, row) => desktop.Apply(row.Name, row.Index, row.Id == currentId));
        Set(ref _current, Find(currentId), nameof(Current));
        Set(ref _supported, true, nameof(Supported));
        Set(ref _reason, "", nameof(UnsupportedReason));
        Refreshed?.Invoke();
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

    public void Dispose()
    {
        _disposed = true;
        _reread?.Dispose();
        _reread = null;
        DesktopSwitchWatch.Fired -= OnSwitched;
    }

    private readonly record struct DesktopRow(string Id, string Name, int Index);
}

/// <summary>One desktop Task View can switch to.</summary>
public sealed class VirtualDesktop : ObservableObject
{
    private string _name = "";
    private int _index;
    private bool _current;

    internal VirtualDesktop(string id) => Id = id;

    /// <summary>The desktop's id, the same text <see cref="AppWindow.DesktopId"/> uses.</summary>
    public string Id { get; }

    public string Name => _name;

    /// <summary>Where the desktop sits in <see cref="VirtualDesktopsService.Desktops"/>, starting at zero.</summary>
    public int Index => _index;

    public bool Current => _current;

    internal void Apply(string name, int index, bool current)
    {
        Set(ref _name, name, nameof(Name));
        Set(ref _index, index, nameof(Index));
        Set(ref _current, current, nameof(Current));
    }
}
