namespace Winshell.Runtime.Shell;

/// <summary>An object whose destruction can be delayed by <see cref="RetainableHook"/>.</summary>
public interface IRetainable
{
    RetainableHook? RetainHook { get; set; }

    /// <summary>Destroys the object. Called once the last lock is gone after it was dropped.</summary>
    void FinishRetain();
}

/// <summary>
/// The <c>Retainable</c> attached object. A lock keeps the object alive after it would have been
/// destroyed, so a closing notification can finish an exit transition.
/// </summary>
[QmlSignal("dropped")]
[QmlSignal("aboutToDestroy")]
public sealed class RetainableHook : QObject
{
    private readonly IRetainable _target;
    private int _refs;
    private int _explicit;
    private bool _inactive = true;
    private bool _finished;

    internal RetainableHook(IRetainable target) => _target = target;

    /// <summary>The hook already attached to <paramref name="obj"/>, creating it when <paramref name="create"/> is set and the object is retainable.</summary>
    public static RetainableHook? Get(QObject obj, bool create)
    {
        if (obj.FindAttached(typeof(RetainableHook)) is RetainableHook existing)
            return existing;
        if (!create || obj is not IRetainable retainable)
            return null;
        var hook = (RetainableHook)obj.GetAttached(typeof(RetainableHook), _ => new RetainableHook(retainable));
        retainable.RetainHook = hook;
        return hook;
    }

    internal static QObject Attach(QObject owner) =>
        Get(owner, create: true) ?? throw new QmlException($"{owner.TypeName} is not Retainable");

    /// <summary>Destroys <paramref name="obj"/>, or holds it while a lock asked it to stay.</summary>
    public static void Release(QObject obj)
    {
        if (obj.IsDestroyed)
            return;
        if (obj is IRetainable retainable && (retainable.RetainHook ?? Get(obj, create: false)) is { } hook)
            hook.Drop();
        else
            obj.Destroy();
    }

    [Qml]
    public bool Retained
    {
        get { Track(); return !_inactive; }
    }

    [Qml]
    public void Lock()
    {
        _explicit++;
        Ref();
    }

    [Qml]
    public void Unlock()
    {
        if (_explicit < 1)
        {
            (Engine?.Log ?? Log.Default).Warn("retainable", "Retainable object unlocked more times than it was locked.");
            return;
        }
        _explicit--;
        Unref();
    }

    /// <summary>Drops every lock. An object that was already dropped is destroyed.</summary>
    [Qml]
    public void ForceUnlock()
    {
        _refs = 0;
        _explicit = 0;
        FinishIfDropped();
    }

    internal void Ref() => _refs++;

    internal void Unref()
    {
        if (_refs == 0)
            return;
        _refs--;
        if (_refs == 0)
            FinishIfDropped();
    }

    internal void Drop()
    {
        if (_finished || !_inactive)
            return;
        Emit("dropped");
        _inactive = false;
        NotifyChanged("retained");
        if (_refs == 0)
            Finish();
    }

    private void FinishIfDropped()
    {
        if (_inactive || _finished)
            return;
        Finish();
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        Emit("aboutToDestroy");
        _target.FinishRetain();
    }
}

/// <summary>The <c>RetainableLock</c>: holds one retainable object while <see cref="Locked"/> is true.</summary>
[QmlSignal("dropped")]
[QmlSignal("aboutToDestroy")]
public sealed class RetainableLock : QObject
{
    private readonly List<IDisposable> _subscriptions = [];
    private QObject? _object;
    private RetainableHook? _hook;
    private bool _locked;

    [Qml]
    public QObject? Object
    {
        get { Track(); return _object; }
        set
        {
            if (ReferenceEquals(value, _object))
                return;
            ReleaseHold();
            if (value is not null)
            {
                var hook = RetainableHook.Get(value, create: true);
                if (hook is null)
                {
                    (Engine?.Log ?? Log.Default).Error("retainable", "Tried to set a non-retainable object as the target of a RetainableLock.");
                }
                else
                {
                    _object = value;
                    _hook = hook;
                    _subscriptions.Add(value.Connect("destroyed", _ => ReleaseHold()));
                    _subscriptions.Add(hook.Connect("dropped", _ => Emit("dropped")));
                    _subscriptions.Add(hook.Connect("aboutToDestroy", _ => Emit("aboutToDestroy")));
                    _subscriptions.Add(hook.Connect("retainedChanged", _ => NotifyChanged("retained")));
                    if (_locked)
                        hook.Ref();
                }
            }
            NotifyChanged("object");
            NotifyChanged("retained");
        }
    }

    [Qml]
    public bool Locked
    {
        get { Track(); return _locked; }
        set
        {
            if (value == _locked)
                return;
            _locked = value;
            if (value)
                _hook?.Ref();
            else
                _hook?.Unref();
            NotifyChanged("locked");
            if (!IsDestroyed)
                NotifyChanged("retained");
        }
    }

    [Qml]
    public bool Retained
    {
        get { Track(); return _hook is not null && !_hook.IsDestroyed && _hook.Retained; }
    }

    protected override void OnDestroy() => ReleaseHold();

    private void ReleaseHold()
    {
        var hook = _hook;
        var locked = _locked;
        _hook = null;
        _object = null;
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
        if (locked)
            hook?.Unref();
    }
}
