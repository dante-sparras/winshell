using Jint;
using Jint.Native;

namespace Winshell.Runtime.QtQml;

/// <summary>QML's <c>QtObject</c>: an object with no behavior of its own, for holding properties.</summary>
public class QtObject : QObject;

/// <summary>QML's <c>Timer</c>.</summary>
[QmlSignal("triggered")]
public sealed class Timer : QObject
{
    private int _interval = 1000;
    private bool _running;
    private bool _repeat;
    private bool _triggeredOnStart;
    private IDisposable? _scheduled;

    [Qml]
    public int Interval
    {
        get { Track(); return _interval; }
        set
        {
            if (SetField(ref _interval, value) && _running)
                Restart();
        }
    }

    [Qml]
    public bool Running
    {
        get { Track(); return _running; }
        set
        {
            if (!SetField(ref _running, value))
                return;
            if (!IsComplete)
                return;
            if (value)
                StartTimer(fireOnStart: _triggeredOnStart);
            else
                CancelTimer();
        }
    }

    [Qml]
    public bool Repeat
    {
        get { Track(); return _repeat; }
        set => SetField(ref _repeat, value);
    }

    [Qml]
    public bool TriggeredOnStart
    {
        get { Track(); return _triggeredOnStart; }
        set => SetField(ref _triggeredOnStart, value);
    }

    [Qml]
    public void Start() => Running = true;

    [Qml]
    public void Stop() => Running = false;

    [Qml]
    public void Restart()
    {
        CancelTimer();
        if (!_running)
        {
            Running = true;
            return;
        }
        StartTimer(fireOnStart: _triggeredOnStart);
    }

    public override void ComponentComplete()
    {
        if (_running)
            StartTimer(fireOnStart: _triggeredOnStart);
    }

    private void StartTimer(bool fireOnStart)
    {
        CancelTimer();
        if (Engine is null)
            return;
        if (fireOnStart)
            Engine.Loop.Post(() => { if (!IsDestroyed && _running) Emit("triggered"); });
        _scheduled = Engine.Loop.Schedule(TimeSpan.FromMilliseconds(Math.Max(0, _interval)), Fire);
    }

    private void Fire()
    {
        _scheduled = null;
        if (IsDestroyed || !_running)
            return;
        if (_repeat)
            _scheduled = Engine!.Loop.Schedule(TimeSpan.FromMilliseconds(Math.Max(1, _interval)), Fire);
        // A one-shot timer reports triggered while it is still running, then runningChanged.
        Emit("triggered");
        if (_repeat || IsDestroyed || !_running || _scheduled is not null)
            return;
        SetField(ref _running, false, nameof(Running));
    }

    private void CancelTimer()
    {
        _scheduled?.Dispose();
        _scheduled = null;
    }

    protected override void OnDestroy() => CancelTimer();
}

/// <summary>
/// QML's <c>Connections</c>: connects its <c>function onSignal()</c> members to the signals of <c>target</c>.
/// </summary>
public sealed class Connections : QObject
{
    private QObject? _target;
    private bool _targetSet;
    private bool _enabled = true;
    private bool _ignoreUnknownSignals;
    private readonly List<IDisposable> _connections = [];
    private readonly List<(string Signal, JsValue Function)> _handlers = [];

    [Qml]
    public QObject? Target
    {
        get { Track(); return _targetSet ? _target : Owner; }
        set
        {
            _targetSet = true;
            SetField(ref _target, value);
            // An explicit target, including null, replaces the owner even when the field was already null.
            Reconnect();
        }
    }

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                Reconnect();
        }
    }

    [Qml]
    public bool IgnoreUnknownSignals
    {
        get { Track(); return _ignoreUnknownSignals; }
        set => SetField(ref _ignoreUnknownSignals, value);
    }

    /// <summary>A handler written as <c>onClicked: ...</c> rather than as a function.</summary>
    internal void AddHandler(string signal, JsValue function) => _handlers.Add((signal, function));

    public override void ComponentComplete() => Reconnect();

    private void Reconnect()
    {
        foreach (var c in _connections)
            c.Dispose();
        _connections.Clear();
        if (!IsComplete || !_enabled || Engine is null)
            return;
        var target = _targetSet ? _target : Owner;
        if (target is null || target.IsDestroyed)
            return;
        var handlers = new List<(string, JsValue)>(_handlers);
        foreach (var name in FunctionNames)
        {
            if (Names.SignalOfHandler(name) is { } signal)
                handlers.Add((signal, GetFunction(name)!));
        }
        var thisValue = Engine.Js.Wrap(this);
        foreach (var (signal, function) in handlers)
        {
            var slot = target.GetSignal(signal) ?? (char.IsUpper(signal[0]) ? null : target.GetSignal(char.ToUpperInvariant(signal[0]) + signal[1..]));
            if (slot is null)
            {
                if (!_ignoreUnknownSignals)
                    Engine.Log.Warn("scene", $"QML Connections: Detected function \"{Names.HandlerName(signal)}\" in Connections element. This is probably intended to be a signal handler but no signal of the target matches the name.");
                continue;
            }
            _connections.Add(slot.ConnectScript(function, thisValue, Engine.Js));
        }
    }

    protected override void OnDestroy()
    {
        foreach (var c in _connections)
            c.Dispose();
        _connections.Clear();
    }
}

/// <summary>QML's <c>Binding</c> element: sets a property of another object while <c>when</c> is true.</summary>
public sealed class BindingElement : QObject
{
    private QObject? _target;
    private string _property = "";
    private object? _value;
    private bool _when = true;
    private bool _delayed;
    private bool _applied;
    private object? _saved;

    [Qml]
    public QObject? Target
    {
        get { Track(); return _target; }
        set
        {
            if (SetField(ref _target, value))
                Apply();
        }
    }

    [Qml]
    public string Property
    {
        get { Track(); return _property; }
        set
        {
            if (SetField(ref _property, value))
                Apply();
        }
    }

    [Qml]
    public object? Value
    {
        get { Track(); return _value; }
        set
        {
            _value = value is JsValue js ? ValueConverter.FromJs(js) ?? (js.IsObject() ? js : null) : value;
            NotifyChanged("value");
            Apply();
        }
    }

    [Qml]
    public bool When
    {
        get { Track(); return _when; }
        set
        {
            if (SetField(ref _when, value))
                Apply();
        }
    }

    [Qml]
    public bool Delayed
    {
        get { Track(); return _delayed; }
        set => SetField(ref _delayed, value);
    }

    [Qml]
    public int RestoreMode { get; set; } = 3;

    public override void ComponentComplete() => Apply();

    private void Apply()
    {
        if (!IsComplete || _target is null || _property.Length == 0)
            return;
        try
        {
            if (_when)
            {
                if (!_applied)
                {
                    using (DependencyTracker.Suspend())
                        _saved = _target.ReadProperty(_property);
                    _applied = true;
                }
                _target.WriteProperty(_property, _value);
            }
            else if (_applied)
            {
                _applied = false;
                _target.WriteProperty(_property, _saved);
            }
        }
        catch (QmlException e)
        {
            Engine?.Log.Warn("scene", $"Binding: {e.Message}");
        }
    }
}
