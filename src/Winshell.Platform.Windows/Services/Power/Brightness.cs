using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Power;

/// <summary>
/// One monitor's brightness. The setter shows the new value at once and writes the panel in the background;
/// the next read after the writes end replaces it. Tests set it only with a fake writer.
/// </summary>
public sealed class BrightnessMonitor : QObject
{
    private readonly BrightnessWriter _writer;
    private BrightnessInfo _info = new("", "", BrightnessConnection.External, 0, 100, 0);
    private int? _requested;

    internal BrightnessMonitor(BrightnessWriter writer) => _writer = writer;

    [Qml] public string DeviceName { get { Track(); return _info.DeviceName; } }

    [Qml] public string Name { get { Track(); return _info.Name; } }

    [Qml] public BrightnessConnection Connection { get { Track(); return _info.Connection; } }

    [Qml] public int Minimum { get { Track(); return _info.Minimum; } }

    [Qml] public int Maximum { get { Track(); return _info.Maximum; } }

    [Qml]
    public int Brightness
    {
        get { Track(); return _requested ?? _info.Current; }
        set
        {
            value = Math.Clamp(value, _info.Minimum, _info.Maximum);
            if (value == (_requested ?? _info.Current))
                return;
            _requested = value;
            NotifyChanged("brightness");
            var log = Engine?.Log ?? Log.Default;
            var name = _info.Name;
            _writer.Request(_info.DeviceName, value, error =>
                log.Warn("power", $"Windows did not set the brightness of {name} (error {error})."));
        }
    }

    internal void Update(BrightnessInfo info)
    {
        var old = _info;
        var oldBrightness = _requested ?? old.Current;
        _info = info;
        if (!_writer.IsWriting(info.DeviceName))
            _requested = null;
        Changed(old.DeviceName != info.DeviceName, "deviceName");
        Changed(old.Name != info.Name, "name");
        Changed(old.Connection != info.Connection, "connection");
        Changed(old.Minimum != info.Minimum, "minimum");
        Changed(old.Maximum != info.Maximum, "maximum");
        Changed(oldBrightness != (_requested ?? info.Current), "brightness");
    }

    private void Changed(bool changed, string property)
    {
        if (changed)
            NotifyChanged(property);
    }
}

/// <summary>The <c>Brightness</c> singleton.</summary>
public sealed class BrightnessService : QObject
{
    private readonly IDisposable _subscription;
    private readonly ObjectModel _monitors = new();
    private readonly List<BrightnessMonitor> _monitorObjects = [];
    private bool _ready;

    internal BrightnessService(QmlEngine engine)
    {
        _monitors.SetOwner(this);
        Apply(BrightnessBackend.Instance.Current);
        _subscription = BrightnessBackend.Instance.Subscribe(s => engine.Loop.Post(() => Apply(s)));
    }

    [Qml] public ObjectModel Monitors => _monitors;

    [Qml] public bool Ready { get { Track(); return _ready; } }

    private void Apply(BrightnessSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        var next = new List<BrightnessMonitor>();
        foreach (var info in snapshot.Monitors)
        {
            var monitor = _monitorObjects.FirstOrDefault(m => m.DeviceName == info.DeviceName && !next.Contains(m));
            if (monitor is null)
            {
                monitor = new BrightnessMonitor(BrightnessBackend.Instance.Writer);
                monitor.SetOwner(this);
            }
            monitor.Update(info);
            next.Add(monitor);
        }
        foreach (var gone in _monitorObjects.Except(next).ToList())
            gone.Destroy();
        _monitorObjects.Clear();
        _monitorObjects.AddRange(next);
        _monitors.Set([.. next]);
        SetField(ref _ready, snapshot.Ready, nameof(Ready));
    }

    protected override void OnDestroy() => _subscription.Dispose();
}
