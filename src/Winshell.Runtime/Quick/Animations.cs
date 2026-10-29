using Jint;
using Jint.Native;

namespace Winshell.Runtime.Quick;

/// <summary>Ticks running animations about 60 times a second while any runs.</summary>
public static class AnimationClock
{
    [ThreadStatic]
    private static List<AnimationBase>? _running;

    [ThreadStatic]
    private static IDisposable? _timer;

    [ThreadStatic]
    private static IEventLoop? _loop;

    public static TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(1000.0 / 60);

    internal static void Add(AnimationBase animation, IEventLoop loop)
    {
        _running ??= [];
        if (!_running.Contains(animation))
            _running.Add(animation);
        // A test disposes its loop before the next frame. The leftover timer belongs to that loop, so a new
        // loop would never tick.
        if (!ReferenceEquals(_loop, loop))
        {
            _timer?.Dispose();
            _timer = null;
            _loop = loop;
        }
        _timer ??= loop.Schedule(FrameInterval, Tick);
    }

    internal static void Remove(AnimationBase animation) => _running?.Remove(animation);

    private static void Tick()
    {
        _timer = null;
        if (_running is null || _running.Count == 0 || _loop is null)
            return;
        var now = _loop.Now;
        try
        {
            foreach (var a in _running.ToArray())
            {
                if (a.IsDestroyed)
                {
                    _running.Remove(a);
                    continue;
                }
                try
                {
                    a.Tick(now);
                }
#pragma warning disable CA1031 // One throwing animation must not stop the clock.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    var log = a.Engine?.Log ?? Log.Default;
                    log.ScriptError(e, a.TypeName);
                    try
                    {
                        a.Stop();
                    }
#pragma warning disable CA1031 // Stopping the failed animation must not stop the clock.
                    catch (Exception stop)
#pragma warning restore CA1031
                    {
                        log.ScriptError(stop, a.TypeName);
                    }
                }
            }
        }
        finally
        {
            if (_running.Count > 0 && _loop is not null)
                _timer = _loop.Schedule(FrameInterval, Tick);
        }
    }
}

/// <summary>An animation's <c>easing</c>: an easing curve, by type or by bezier points.</summary>
public sealed class EasingGroup : QObject
{
    private int _type;
    private double _amplitude = 1;
    private double _overshoot = 1.70158;
    private double _period = 0.3;
    private double[] _bezier = [];

    [Qml] public int Type { get { Track(); return _type; } set => SetField(ref _type, value); }

    [Qml] public double Amplitude { get { Track(); return _amplitude; } set => SetField(ref _amplitude, value); }

    [Qml] public double Overshoot { get { Track(); return _overshoot; } set => SetField(ref _overshoot, value); }

    [Qml] public double Period { get { Track(); return _period; } set => SetField(ref _period, value); }

    [Qml]
    public object? BezierCurve
    {
        get { Track(); return _bezier.Cast<object?>().ToList(); }
        set
        {
            _bezier = ValueConverter.ToList(value).Select(v => ValueConverter.ToDouble(v, "real")).ToArray();
            NotifyChanged("bezierCurve");
        }
    }

    public double Value(double t) => Easing.Value(_type, t, _amplitude, _overshoot, _period, _bezier);
}

/// <summary>QtQuick's <c>Easing</c> enum and the curves it names.</summary>
public static class Easing
{
    public static readonly string[] Names =
    [
        "Linear", "InQuad", "OutQuad", "InOutQuad", "OutInQuad", "InCubic", "OutCubic", "InOutCubic", "OutInCubic",
        "InQuart", "OutQuart", "InOutQuart", "OutInQuart", "InQuint", "OutQuint", "InOutQuint", "OutInQuint",
        "InSine", "OutSine", "InOutSine", "OutInSine", "InExpo", "OutExpo", "InOutExpo", "OutInExpo",
        "InCirc", "OutCirc", "InOutCirc", "OutInCirc", "InElastic", "OutElastic", "InOutElastic", "OutInElastic",
        "InBack", "OutBack", "InOutBack", "OutInBack", "InBounce", "OutBounce", "InOutBounce", "OutInBounce",
        "InCurve", "OutCurve", "SineCurve", "CosineCurve", "BezierSpline",
    ];

    public static IEnumerable<KeyValuePair<string, int>> Enums => Names.Select((n, i) => new KeyValuePair<string, int>(n, i));

    public const int BezierSpline = 45;

    public static double Value(int type, double t, double amplitude = 1, double overshoot = 1.70158, double period = 0.3, double[]? bezier = null)
    {
        t = Math.Clamp(t, 0, 1);
        if (type == BezierSpline && bezier is { Length: >= 6 })
            return BezierValue(bezier, t);
        var group = (type - 1) / 4;
        var variant = (type - 1) % 4;
        if (type == 0 || type > 40)
        {
            return type switch
            {
                41 => t * t,
                42 => Math.Sqrt(t),
                43 => (Math.Sin(t * 2 * Math.PI - Math.PI / 2) + 1) / 2,
                44 => (Math.Cos(t * 2 * Math.PI) + 1) / 2,
                _ => t,
            };
        }
        Func<double, double> easeIn = group switch
        {
            0 => x => x * x,
            1 => x => x * x * x,
            2 => x => x * x * x * x,
            3 => x => x * x * x * x * x,
            4 => x => 1 - Math.Cos(x * Math.PI / 2),
            5 => x => x == 0 ? 0 : Math.Pow(2, 10 * (x - 1)) - 0.001,
            6 => x => -(Math.Sqrt(1 - x * x) - 1),
            7 => x => Elastic(x, amplitude, period),
            8 => x => x * x * ((overshoot + 1) * x - overshoot),
            _ => x => 1 - BounceOut(1 - x, amplitude),
        };
        double EaseOut(double x) => 1 - easeIn(1 - x);
        return variant switch
        {
            0 => easeIn(t),
            1 => EaseOut(t),
            2 => t < 0.5 ? easeIn(t * 2) / 2 : 0.5 + EaseOut(t * 2 - 1) / 2,
            _ => t < 0.5 ? EaseOut(t * 2) / 2 : 0.5 + easeIn(t * 2 - 1) / 2,
        };
    }

    private static double Elastic(double t, double a, double p)
    {
        if (t == 0 || t == 1)
            return t;
        double s;
        if (a < 1)
        {
            a = 1;
            s = p / 4;
        }
        else
        {
            s = p / (2 * Math.PI) * Math.Asin(1 / a);
        }
        t -= 1;
        return -(a * Math.Pow(2, 10 * t) * Math.Sin((t - s) * (2 * Math.PI) / p));
    }

    private static double BounceOut(double t, double a)
    {
        if (t < 1 / 2.75)
            return 7.5625 * t * t;
        if (t < 2 / 2.75)
        {
            t -= 1.5 / 2.75;
            return -a * (1 - (7.5625 * t * t + .75)) + 1;
        }
        if (t < 2.5 / 2.75)
        {
            t -= 2.25 / 2.75;
            return -a * (1 - (7.5625 * t * t + .9375)) + 1;
        }
        t -= 2.625 / 2.75;
        return -a * (1 - (7.5625 * t * t + .984375)) + 1;
    }

    /// <summary>A curve of cubic segments: (c1x, c1y, c2x, c2y, endx, endy) repeated, from (0,0) to (1,1).</summary>
    private static double BezierValue(double[] points, double t)
    {
        double sx = 0, sy = 0;
        for (var i = 0; i + 5 < points.Length; i += 6)
        {
            var ex = points[i + 4];
            var ey = points[i + 5];
            if (t <= ex || i + 11 >= points.Length)
            {
                var segT = ex - sx == 0 ? 1 : (t - sx) / (ex - sx);
                var u = SolveX(segT, sx, points[i], points[i + 2], ex, t);
                return Cubic(u, sy, points[i + 1], points[i + 3], ey);
            }
            sx = ex;
            sy = ey;
        }
        return t;
    }

    private static double Cubic(double u, double p0, double p1, double p2, double p3)
    {
        var v = 1 - u;
        return v * v * v * p0 + 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u * p3;
    }

    private static double SolveX(double guess, double x0, double x1, double x2, double x3, double x)
    {
        double lo = 0, hi = 1, u = Math.Clamp(guess, 0, 1);
        for (var i = 0; i < 30; i++)
        {
            var cx = Cubic(u, x0, x1, x2, x3);
            if (Math.Abs(cx - x) < 1e-6)
                break;
            if (cx < x)
                lo = u;
            else
                hi = u;
            u = (lo + hi) / 2;
        }
        return u;
    }
}

/// <summary>The base of every animation.</summary>
[QmlSignal("started")]
[QmlSignal("stopped")]
[QmlSignal("finished")]
public abstract class AnimationBase : QObject, IPropertyValueSource
{
    private bool _running;
    private bool _paused;
    private TimeSpan _pausedAt;
    private int _loops = 1;
    private bool _alwaysRunToEnd;
    private TimeSpan _start;
    private int _loopsDone;
    private bool _runningSetBeforeComplete;

    public const int Infinite = -1;

    [Qml]
    public bool Running
    {
        get { Track(); return _running; }
        set
        {
            if (!IsComplete && Group is null)
            {
                _runningSetBeforeComplete = value;
                SetField(ref _running, value);
                return;
            }
            if (value)
                Start();
            else
                Stop();
        }
    }

    [Qml]
    public bool Paused
    {
        get { Track(); return _paused; }
        set
        {
            if (value == _paused)
                return;
            var now = Engine?.Loop.Now ?? TimeSpan.Zero;
            if (value)
                _pausedAt = now;
            else
                _start += now - _pausedAt;
            SetField(ref _paused, value);
        }
    }

    [Qml]
    public int Loops
    {
        get { Track(); return _loops; }
        set => SetField(ref _loops, value);
    }

    [Qml]
    public bool AlwaysRunToEnd
    {
        get { Track(); return _alwaysRunToEnd; }
        set => SetField(ref _alwaysRunToEnd, value);
    }

    /// <summary>The group this animation is part of; a group runs its members itself.</summary>
    internal AnimationGroup? Group { get; set; }

    /// <summary>How long one loop takes, or null when the animation decides as it runs.</summary>
    public abstract double DurationMs { get; }

    [Qml]
    public void Start()
    {
        if (_running && Group is null && _ticking)
            return;
        _loopsDone = 0;
        Prepare();
        _start = Engine?.Loop.Now ?? TimeSpan.Zero;
        SetField(ref _running, true, nameof(Running));
        Emit("started");
        if (Group is null && Engine is not null)
        {
            _ticking = true;
            AnimationClock.Add(this, Engine.Loop);
            if (DurationMs <= 0)
                Tick(_start);
        }
    }

    private bool _ticking;

    [Qml]
    public void Stop()
    {
        if (!_running)
            return;
        _ticking = false;
        AnimationClock.Remove(this);
        SetField(ref _running, false, nameof(Running));
        Emit("stopped");
    }

    [Qml]
    public void Restart()
    {
        Stop();
        Start();
    }

    [Qml]
    public void Complete()
    {
        if (!_running)
            return;
        Apply(1);
        Finish();
    }

    [Qml]
    public void Pause() => Paused = true;

    [Qml]
    public void Resume() => Paused = false;

    internal void Tick(TimeSpan now)
    {
        if (!_running || _paused)
            return;
        var duration = DurationMs;
        var elapsed = (now - _start).TotalMilliseconds;
        if (duration <= 0)
        {
            Apply(1);
            Finish();
            return;
        }
        var progress = elapsed / duration;
        if (progress >= 1)
        {
            _loopsDone++;
            if (_loops == Infinite || _loopsDone < _loops)
            {
                Apply(1);
                _start = now;
                Prepare();
                Apply(0);
                return;
            }
            Apply(1);
            Finish();
            return;
        }
        Apply(progress);
    }

    private void Finish()
    {
        _ticking = false;
        AnimationClock.Remove(this);
        SetField(ref _running, false, nameof(Running));
        Emit("stopped");
        Emit("finished");
    }

    /// <summary>Reads the start values before a run.</summary>
    internal virtual void Prepare()
    {
    }

    /// <summary>Sets the animated values for progress from 0 to 1.</summary>
    internal abstract void Apply(double progress);

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        if (_runningSetBeforeComplete && Group is null)
        {
            _running = false;
            Start();
        }
    }

    // "NumberAnimation on x { }" runs on that property from the start.
    public virtual void SetTarget(QObject target, string property)
    {
        if (this is PropertyAnimation pa)
        {
            pa.Target = target;
            pa.Property = property;
            _runningSetBeforeComplete = true;
            SetField(ref _running, true, nameof(Running));
        }
    }

    protected override void OnDestroy()
    {
        AnimationClock.Remove(this);
        base.OnDestroy();
    }
}

/// <summary>QtQuick's <c>PropertyAnimation</c>, and the base of NumberAnimation and ColorAnimation.</summary>
public class PropertyAnimation : AnimationBase
{
    private QObject? _target;
    private string _property = "";
    private string _properties = "";
    private object? _from;
    private object? _to;
    private double _duration = 250;
    private readonly List<(QObject Target, string Property, object? From, object? To)> _tracks = [];

    public PropertyAnimation()
    {
        Easing = new EasingGroup();
    }

    [Qml]
    public QObject? Target { get { Track(); return _target; } set => SetField(ref _target, value); }

    [Qml]
    public object? Targets { get; set; }

    [Qml]
    public string Property { get { Track(); return _property; } set => SetField(ref _property, value ?? ""); }

    [Qml]
    public string Properties { get { Track(); return _properties; } set => SetField(ref _properties, value ?? ""); }

    [Qml]
    public object? From
    {
        get { Track(); return _from; }
        set
        {
            _from = ValueConverter.Unwrap(value);
            NotifyChanged("from");
        }
    }

    [Qml]
    public object? To
    {
        get { Track(); return _to; }
        set
        {
            _to = ValueConverter.Unwrap(value);
            NotifyChanged("to");
        }
    }

    [Qml]
    public double Duration { get { Track(); return _duration; } set => SetField(ref _duration, value); }

    [Qml]
    public EasingGroup Easing { get; }

    [Qml]
    public object? Exclude { get; set; }

    public override double DurationMs => _duration;

    /// <summary>The value the animation goes to, when a Behavior or Transition sets it.</summary>
    internal object? OverrideTo { get; set; }

    internal object? OverrideFrom { get; set; }

    internal (QObject Target, string Property)? OverrideTrack { get; set; }

    /// <summary>The changes of a state this animation runs for a Transition, each to the value the state sets.</summary>
    internal List<(QObject Target, string Property, object? To)> TransitionTracks { get; } = [];

    /// <summary>Whether this animation animates a property: its target and properties name it, or name nothing.</summary>
    internal bool Animates(QObject target, string property)
    {
        if (_target is not null && _target != target)
            return false;
        if (Targets is not null && !ValueConverter.ToList(Targets).Contains(target))
            return false;
        var names = _properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_property.Length == 0 && names.Length == 0)
            return true;
        return _property == property || names.Contains(property);
    }

    internal override void Prepare()
    {
        _tracks.Clear();
        using var _ = DependencyTracker.Suspend();
        var targets = new List<QObject>();
        if (TransitionTracks.Count > 0)
        {
            foreach (var (target, property, to) in TransitionTracks)
            {
                if (target.HasProperty(property))
                    _tracks.Add((target, property, OverrideFrom ?? _from ?? target.ReadProperty(property), to));
            }
            return;
        }
        if (OverrideTrack is { } single)
        {
            Add(single.Target, single.Property);
            return;
        }
        if (_target is not null)
            targets.Add(_target);
        foreach (var t in ValueConverter.ToList(Targets))
        {
            if (t is QObject o)
                targets.Add(o);
        }
        var props = new List<string>();
        if (_property.Length > 0)
            props.Add(_property);
        props.AddRange(_properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var t in targets)
        {
            foreach (var p in props)
                Add(t, p);
        }

        void Add(QObject target, string property)
        {
            if (!target.HasProperty(property))
                return;
            var from = OverrideFrom ?? _from ?? target.ReadProperty(property);
            var to = OverrideTo ?? _to ?? target.ReadProperty(property);
            _tracks.Add((target, property, from, to));
        }
    }

    internal override void Apply(double progress)
    {
        var eased = Easing.Value(progress);
        foreach (var (target, property, from, to) in _tracks)
        {
            if (target.IsDestroyed)
                continue;
            object? value;
            try
            {
                value = Interpolate(from, to, eased);
                target.WritePropertyDirect(property, value);
            }
#pragma warning disable CA1031 // A bad interpolation is a script error, not a dead clock.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Engine?.Log.Warn("scene", $"{TypeName}: {e.Message}");
            }
        }
    }

    protected virtual object? Interpolate(object? from, object? to, double t)
    {
        if (from is QColor || to is QColor || (from is string fs && QColor.TryParse(fs, out _)) || (to is string ts && QColor.TryParse(ts, out _)))
        {
            var a = ValueConverter.ToColor(from);
            var b = ValueConverter.ToColor(to);
            if (!a.IsValid)
                a = b.WithAlpha(0);
            return QColor.Blend(a, b, t);
        }
        if (IsNumber(from) && IsNumber(to))
        {
            var a = ValueConverter.ToDouble(from, "real");
            var b = ValueConverter.ToDouble(to, "real");
            return a + (b - a) * t;
        }
        return t >= 1 ? to : from;
    }

    private static bool IsNumber(object? v) => v is double or int or float or long;
}

public sealed class NumberAnimation : PropertyAnimation;

public sealed class ColorAnimation : PropertyAnimation;

/// <summary>QtQuick's <c>RotationAnimation</c>: turns the shorter way when asked.</summary>
public sealed class RotationAnimation : PropertyAnimation
{
    private int _direction;

    [QmlEnum]
    public enum RotationDirection
    {
        Numerical = 0,
        Clockwise = 1,
        Counterclockwise = 2,
        Shortest = 3,
    }

    [Qml]
    public int Direction { get { Track(); return _direction; } set => SetField(ref _direction, value); }

    protected override object? Interpolate(object? from, object? to, double t)
    {
        var a = ValueConverter.ToDouble(from ?? 0.0, "real");
        var b = ValueConverter.ToDouble(to ?? 0.0, "real");
        if (_direction == (int)RotationDirection.Shortest)
        {
            var d = ((b - a) % 360 + 540) % 360 - 180;
            b = a + d;
        }
        return a + (b - a) * t;
    }
}

/// <summary>QtQuick's <c>SmoothedAnimation</c>: moves at a velocity, or for a duration.</summary>
public sealed class SmoothedAnimation : PropertyAnimation
{
    [Qml]
    public double Velocity { get; set; } = 200;

    [Qml]
    public int ReversingMode { get; set; }

    [Qml]
    public double MaximumEasingTime { get; set; } = -1;

    public override double DurationMs
    {
        get
        {
            if (Duration > 0 && Duration != 250)
                return Duration;
            if (OverrideTrack is { } t && OverrideTo is not null && Velocity > 0)
            {
                using var _ = DependencyTracker.Suspend();
                var from = ValueConverter.ToDouble(t.Target.ReadProperty(t.Property), "real");
                var to = ValueConverter.ToDouble(OverrideTo, "real");
                return Math.Abs(to - from) / Velocity * 1000;
            }
            return Duration;
        }
    }
}

/// <summary>QtQuick's <c>SpringAnimation</c>, approximated by an eased move of a fitting length.</summary>
public sealed class SpringAnimation : PropertyAnimation
{
    public SpringAnimation()
    {
        Easing.Type = 2;
    }

    [Qml] public double Spring { get; set; } = 3;

    [Qml] public double Damping { get; set; } = 0.2;

    [Qml] public double Epsilon { get; set; } = 0.01;

    [Qml] public double Mass { get; set; } = 1;

    [Qml] public double Velocity { get; set; }

    [Qml] public double Modulus { get; set; }

    public override double DurationMs => Math.Clamp(1500 / Math.Max(0.5, Spring) * (1 - Damping / 2), 100, 2000);
}

/// <summary>QtQuick's <c>PauseAnimation</c>.</summary>
public sealed class PauseAnimation : AnimationBase
{
    private double _duration = 250;

    [Qml]
    public double Duration { get { Track(); return _duration; } set => SetField(ref _duration, value); }

    public override double DurationMs => _duration;

    internal override void Apply(double progress)
    {
    }
}

/// <summary>QtQuick's <c>ScriptAction</c>: runs a script when the animation reaches it.</summary>
public sealed class ScriptAction : AnimationBase
{
    private JsValue? _script;
    private bool _done;

    [Qml]
    public object? Script
    {
        get => _script;
        set => _script = value as JsValue;
    }

    [Qml]
    public string ScriptName { get; set; } = "";

    public override double DurationMs => 0;

    internal override void Prepare() => _done = false;

    internal override void Apply(double progress)
    {
        if (_done)
            return;
        _done = true;
        if (_script is null || !_script.IsCallable() || Engine is null)
            return;
        try
        {
            Engine.Js.Engine.Call(_script, JsValue.Undefined, []);
        }
#pragma warning disable CA1031 // A script error is logged. The animation still finishes.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Engine.Log.ScriptError(e, TypeName);
        }
    }
}

/// <summary>QtQuick's <c>PropertyAction</c>: sets a property at once.</summary>
public sealed class PropertyAction : AnimationBase
{
    [Qml] public QObject? Target { get; set; }

    [Qml] public string Property { get; set; } = "";

    [Qml] public object? Value { get; set; }

    public override double DurationMs => 0;

    /// <summary>The state changes this action applies when a Transition reaches it.</summary>
    internal List<(QObject Target, string Property, object? To)> TransitionTracks { get; } = [];

    /// <summary>Whether this action sets a property: its target and property name it, or name nothing.</summary>
    internal bool Animates(QObject target, string property)
    {
        if (Target is not null && !ReferenceEquals(Target, target))
            return false;
        if (Property.Length == 0)
            return true;
        return Property == property;
    }

    internal override void Apply(double progress)
    {
        if (progress < 1)
            return;
        if (TransitionTracks.Count > 0)
        {
            foreach (var (target, property, to) in TransitionTracks)
                Write(target, property, UsesValue(target, property) ? Value : to);
            return;
        }
        if (Target is null || Property.Length == 0 || Value is null)
            return;
        Write(Target, Property, Value);
    }

    private bool UsesValue(QObject target, string property) =>
        Value is not null
        && (Property.Length == 0 || Property == property)
        && (Target is null || ReferenceEquals(Target, target));

    private void Write(QObject target, string property, object? value)
    {
        if (target.IsDestroyed || property.Length == 0 || !target.HasProperty(property))
            return;
        try
        {
            target.WritePropertyDirect(property, value);
        }
#pragma warning disable CA1031 // A bad write is a script error, not a dead clock.
        catch (Exception e)
#pragma warning restore CA1031
        {
            Engine?.Log.Warn("scene", $"{TypeName}: {e.Message}");
        }
    }
}

/// <summary>The base of SequentialAnimation and ParallelAnimation.</summary>
[QmlDefaultProperty("animations")]
public abstract class AnimationGroup : AnimationBase
{
    protected AnimationGroup()
    {
        Animations = new QmlList<AnimationBase>(this, "animations", added: a =>
        {
            a.Group = this;
            a.SetOwner(this);
        });
    }

    [Qml]
    public QmlList<AnimationBase> Animations { get; }

    /// <summary>Gives each member the value a Behavior or Transition asks for.</summary>
    internal void Override(QObject target, string property, object? to, object? from)
    {
        foreach (var a in Animations.Snapshot)
        {
            if (a is PropertyAnimation pa && pa.Animates(target, property))
            {
                pa.OverrideTrack = (target, property);
                pa.OverrideTo = to;
                pa.OverrideFrom = from;
            }
            else if (a is AnimationGroup g)
            {
                g.Override(target, property, to, from);
            }
        }
    }
}

public sealed class SequentialAnimation : AnimationGroup
{
    public override double DurationMs => Animations.Snapshot.Sum(a => Math.Max(0, a.DurationMs) * Math.Max(1, a.Loops));

    private int _current = -1;

    internal override void Prepare() => _current = -1;

    internal override void Apply(double progress)
    {
        var total = DurationMs;
        var elapsed = progress * total;
        var t = 0.0;
        var members = Animations.Snapshot;
        for (var i = 0; i < members.Count; i++)
        {
            var a = members[i];
            var d = Math.Max(0, a.DurationMs) * Math.Max(1, a.Loops);
            if (i > _current && elapsed >= t)
            {
                for (var k = _current + 1; k <= i; k++)
                {
                    if (k < i)
                    {
                        members[k].Prepare();
                        members[k].Apply(1);
                    }
                    else
                    {
                        members[k].Prepare();
                    }
                }
                _current = i;
            }
            if (elapsed < t + d || (d == 0 && elapsed >= t && i == _current))
            {
                if (i == _current)
                    a.Apply(d <= 0 ? 1 : (elapsed - t) / d % 1.0000001);
                if (elapsed < t + d)
                    return;
            }
            t += d;
        }
        if (progress >= 1 && members.Count > 0)
            members[^1].Apply(1);
    }
}

public sealed class ParallelAnimation : AnimationGroup
{
    public override double DurationMs => Animations.Snapshot.Count == 0 ? 0 : Animations.Snapshot.Max(a => Math.Max(0, a.DurationMs) * Math.Max(1, a.Loops));

    internal override void Prepare()
    {
        foreach (var a in Animations.Snapshot)
            a.Prepare();
    }

    internal override void Apply(double progress)
    {
        var total = DurationMs;
        var elapsed = progress * total;
        foreach (var a in Animations.Snapshot)
        {
            var d = Math.Max(0, a.DurationMs);
            a.Apply(d <= 0 ? 1 : Math.Min(1, elapsed / d));
        }
    }
}

/// <summary>
/// QtQuick's <c>Behavior</c>: animates every change to a property. While its component is being created, values
/// are set without animating.
/// </summary>
[QmlDefaultProperty("animation")]
public sealed class Behavior : QObject, IPropertyValueSource, IPropertyInterceptor
{
    private AnimationBase? _animation;
    private bool _enabled = true;
    private QObject? _target;
    private string _property = "";
    private object? _targetValue;

    [Qml]
    public AnimationBase? Animation
    {
        get => _animation;
        set
        {
            _animation = value;
            if (value is not null)
                value.SetOwner(this);
        }
    }

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled && IsComplete && (_target?.IsComplete ?? false); }
        set => SetField(ref _enabled, value);
    }

    [Qml]
    public object? TargetValue => _targetValue;

    [Qml]
    public QObject? TargetProperty => _target;

    public void SetTarget(QObject target, string property)
    {
        _target = target;
        _property = property;
        target.SetInterceptor(property, this);
    }

    public void Intercept(object? value)
    {
        if (_target is null)
            return;
        _targetValue = value;
        if (_animation is null)
        {
            _target.WritePropertyDirect(_property, value);
            return;
        }
        object? current;
        using (DependencyTracker.Suspend())
            current = _target.ReadProperty(_property);
        var to = ConvertLike(value);
        if (ValueConverter.AreEqual(current, to))
            return;
        _animation.Stop();
        if (_animation is PropertyAnimation pa)
        {
            pa.OverrideTrack = (_target, _property);
            pa.OverrideTo = to;
            pa.OverrideFrom = null;
        }
        else if (_animation is AnimationGroup g)
        {
            g.Override(_target, _property, to, null);
        }
        _animation.Start();
    }

    private object? ConvertLike(object? value)
    {
        var type = _target!.PropertyClrType(_property);
        if (type is not null)
            return ValueConverter.Convert(value, type, Engine);
        if (_target.GetDynamicProperty(_property) is { } dp)
            return ValueConverter.ConvertToQmlType(value, dp.QmlType, Engine);
        return ValueConverter.Unwrap(value);
    }
}
