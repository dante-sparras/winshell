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

    public string DeviceName => _info.DeviceName;

    public string Name => _info.Name;

    public BrightnessConnection Connection => _info.Connection;

    public int Minimum => _info.Minimum;

    public int Maximum => _info.Maximum;

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
            _writer.Request(_info.DeviceName, value, error =>
                _log.Warn("power", $"Windows did not set the brightness of {name} (error {error})."));
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

    internal BrightnessService(IEventLoop loop, Log log, SnapshotSource<BrightnessSnapshot>? source = null, BrightnessWriter? writer = null)
    {
        _log = log;
        source ??= BrightnessBackend.Instance;
        _writer = writer ?? BrightnessBackend.Instance.Writer;
        Apply(source.Current);
        _subscription = source.Subscribe(s => loop.Post(() => Apply(s)));
    }

    public ObservableCollection<BrightnessMonitor> Monitors { get; } = [];

    public bool Ready => _ready;

    private void Apply(BrightnessSnapshot snapshot)
    {
        if (_disposed)
            return;
        Monitors.Sync(snapshot.Monitors, m => m.DeviceName, i => i.DeviceName, _ => new BrightnessMonitor(_writer, _log), (m, i) => m.Update(i));
        Set(ref _ready, snapshot.Ready, nameof(Ready));
    }

    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
    }
}
