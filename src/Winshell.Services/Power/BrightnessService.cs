using System.Collections.ObjectModel;
using System.Collections.Specialized;

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
    private readonly IBrightnessBackend _backend;
    private readonly INotifyCollectionChanged _screens;
    private readonly StateReader<BrightnessSnapshot> _reader;
    private readonly BrightnessWriter _writer;
    private readonly Log _log;
    private bool _ready;

    /// <param name="loop">The loop readings are applied on.</param>
    /// <param name="log">Where a failed read or write is reported.</param>
    /// <param name="screens">The shell's screens. When they change, a monitor came, went or changed mode, so the monitors are read again.</param>
    /// <param name="backend">Windows, or a fake in tests.</param>
    internal BrightnessService(IEventLoop loop, Log log, INotifyCollectionChanged screens,
        IBrightnessBackend? backend = null)
    {
        _log = log;
        _screens = screens;
        _backend = backend ?? new BrightnessBackend(log);
        _reader = new StateReader<BrightnessSnapshot>(_backend.ReadAsync, Apply, loop,
            new MonitorWarning(log, "power"));
        // A monitor's read after its writes end shows what it took.
        _writer = new BrightnessWriter(_backend.Set, log, _reader.Request);
        _backend.Changed += _reader.Request;
        _screens.CollectionChanged += OnScreensChanged;
        _reader.Request();
    }

    /// <summary>The connected monitors whose brightness Windows can read. Empty until <see cref="Ready"/>.</summary>
    public ObservableCollection<BrightnessMonitor> Monitors { get; } = [];

    /// <summary>Whether the first read of the monitors has finished.</summary>
    public bool Ready => _ready;

    private void Apply(BrightnessSnapshot snapshot)
    {
        Monitors.Sync(snapshot.Monitors, m => m.DeviceName, i => i.DeviceName,
            _ => new BrightnessMonitor(_writer, _log), (m, i) => m.Update(i));
        Set(ref _ready, snapshot.Ready, nameof(Ready));
    }

    private void OnScreensChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _reader.Request();
    }

    /// <summary>Stops following brightness changes.</summary>
    public void Dispose()
    {
        _screens.CollectionChanged -= OnScreensChanged;
        _backend.Changed -= _reader.Request;
        _reader.Stop(_backend.Dispose);
    }
}
