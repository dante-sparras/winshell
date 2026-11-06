using System.Collections.ObjectModel;

namespace Winshell.Power;

/// <summary>
/// One monitor's brightness. The setter shows the new value at once and writes the panel in the background;
/// the next read after the writes end replaces it.
/// </summary>
public sealed class BrightnessMonitor : ObservableObject
{
    private readonly BrightnessWriter _writer;
    private readonly Log _log;
    private BrightnessInfo _info = new("", "", BrightnessConnection.External, 0, 100, 0);
    private int? _requested;

    internal BrightnessMonitor(BrightnessWriter writer, Log log)
    {
        _writer = writer;
        _log = log;
    }

    /// <summary>
    /// The name Windows knows the monitor by: a WMI instance name for a built-in panel, a display name like
    /// <c>\\.\DISPLAY1</c> for an external monitor.
    /// </summary>
    public string DeviceName => _info.DeviceName;

    /// <summary>The monitor's model name, or its Windows device name when the monitor reports no model.</summary>
    public string Name => _info.Name;

    /// <summary>Whether the monitor is a built-in panel or an external monitor.</summary>
    public BrightnessConnection Connection => _info.Connection;

    /// <summary>The lowest brightness the monitor accepts.</summary>
    public int Minimum => _info.Minimum;

    /// <summary>The highest brightness the monitor accepts, often 100.</summary>
    public int Maximum => _info.Maximum;

    /// <summary>
    /// The brightness, from <see cref="Minimum"/> to <see cref="Maximum"/>. A value set outside that range is clamped
    /// to it. A write Windows refuses is logged, and the next read shows the brightness the monitor has.
    /// </summary>
    public int Brightness
    {
        get => _requested ?? _info.Current;
        set
        {
            value = Math.Clamp(value, _info.Minimum, _info.Maximum);
            if (value == (_requested ?? _info.Current))
                return;
            _requested = value;
            Changed();
            var name = _info.Name;
            _writer.Request(_info.DeviceName, value, reason =>
                _log.Warn("power", $"Windows did not set the brightness of {name}: {reason}."));
        }
    }

    internal void Update(BrightnessInfo info)
    {
        var old = _info;
        var oldBrightness = _requested ?? old.Current;
        _info = info;
        if (!_writer.IsWriting(info.DeviceName))
            _requested = null;
        Changed(old.DeviceName != info.DeviceName, nameof(DeviceName));
        Changed(old.Name != info.Name, nameof(Name));
        Changed(old.Connection != info.Connection, nameof(Connection));
        Changed(old.Minimum != info.Minimum, nameof(Minimum));
        Changed(old.Maximum != info.Maximum, nameof(Maximum));
        Changed(oldBrightness != (_requested ?? info.Current), nameof(Brightness));
    }
}

/// <summary>The brightness of the connected monitors that report one.</summary>
public sealed class BrightnessService : ObservableObject, IDisposable
{
    private readonly IDisposable _subscription;
    private readonly BrightnessWriter _writer;
    private readonly Log _log;
    private bool _ready;
    private bool _disposed;

    internal BrightnessService(IEventLoop loop, Log log, SnapshotSource<BrightnessSnapshot>? source = null,
        BrightnessWriter? writer = null)
    {
        _log = log;
        source ??= BrightnessBackend.Instance;
        _writer = writer ?? BrightnessBackend.Instance.Writer;
        Apply(source.Current);
        _subscription = source.Subscribe(s => loop.Post(() => Apply(s)));
    }

    /// <summary>The connected monitors whose brightness Windows can read. Empty until <see cref="Ready"/>.</summary>
    public ObservableCollection<BrightnessMonitor> Monitors { get; } = [];

    /// <summary>Whether the first read of the monitors has finished.</summary>
    public bool Ready => _ready;

    private void Apply(BrightnessSnapshot snapshot)
    {
        if (_disposed)
            return;
        Monitors.Sync(snapshot.Monitors, m => m.DeviceName, i => i.DeviceName,
            _ => new BrightnessMonitor(_writer, _log), (m, i) => m.Update(i));
        Set(ref _ready, snapshot.Ready, nameof(Ready));
    }

    /// <summary>Stops following brightness changes.</summary>
    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
    }
}
