using Jint.Native;

namespace Winshell.Runtime;

/// <summary>The handlers connected to one signal of one object.</summary>
public sealed class SignalSlot(QObject owner, SignalDefinition definition)
{
    private readonly List<Handler> _handlers = [];

    public QObject Owner { get; } = owner;

    public SignalDefinition Definition { get; } = definition;

    public int Count => _handlers.Count;

    public IDisposable Connect(Action<object?[]> handler) => Add(new Handler(handler, null, null, null, null, null));

    /// <summary>
    /// Connects a JavaScript function of <paramref name="runtime"/>, which runs it. <paramref name="thisValue"/> is
    /// its <c>this</c>. The connection ends when the runtime's engine is disposed, or when the function's scope
    /// object is destroyed, as the signal's object may live on.
    /// </summary>
    public IDisposable ConnectScript(JsValue function, JsValue? thisValue, JsRuntime runtime) =>
        Add(new Handler(null, function, thisValue, runtime, ReceiverOf(thisValue) ?? runtime.CurrentScope, null));

    /// <summary>Connects a C# handler made by script code of <paramref name="runtime"/>, such as a signal connected to a signal.</summary>
    public IDisposable ConnectFor(JsRuntime runtime, Action<object?[]> handler, JsValue? signalKey = null) =>
        Add(new Handler(handler, null, null, runtime, runtime.CurrentScope, signalKey));

    private static QObject? ReceiverOf(JsValue? thisValue) => thisValue is QObjectWrapper wrapper ? wrapper.Target : null;

    private Disconnect Add(Handler handler)
    {
        _handlers.Add(handler);
        handler.Runtime?.Qml.ConnectedTo(this);
        return new Disconnect(this, handler);
    }

    /// <summary>Removes the handlers a runtime connected, when its engine is disposed.</summary>
    internal void DisconnectRuntime(JsRuntime runtime)
    {
        foreach (var h in _handlers)
        {
            if (h.Runtime == runtime)
                h.Removed = true;
        }
        _handlers.RemoveAll(h => h.Runtime == runtime);
    }

    public bool DisconnectScript(JsValue function)
    {
        var found = false;
        foreach (var h in _handlers)
        {
            if (h.Removed)
                continue;
            var script = h.Script is not null && ReferenceEquals(h.Script, function);
            var signal = h.SignalKey is not null && ReferenceEquals(h.SignalKey, function);
            if (!script && !signal)
                continue;
            h.Removed = true;
            found = true;
        }
        if (found)
            _handlers.RemoveAll(h => h.Removed);
        return found;
    }

    public void Emit(object?[] args)
    {
        if (_handlers.Count == 0)
            return;
        foreach (var h in _handlers.ToArray())
        {
            if (Owner.IsDestroyed)
                return;
            if (h.Removed)
                continue;
            if (h.Receiver is { IsDestroyed: true })
            {
                h.Removed = true;
                continue;
            }
            try
            {
                if (h.Native is not null)
                {
                    h.Native(args);
                }
                else if ((h.Runtime ?? Owner.Engine?.Js) is { Qml.IsDisposed: false } runtime)
                {
                    runtime.CallFunction(h.Script!, h.This, args);
                }
            }
            catch (Exception e)
            {
                (Owner.Engine?.Log ?? Log.Default).ScriptError(e, $"{Owner.TypeName}.{Definition.Name}");
            }
        }
        _handlers.RemoveAll(h => h.Removed);
    }

    public void Clear()
    {
        foreach (var h in _handlers)
            h.Removed = true;
        _handlers.Clear();
    }

    private sealed class Handler(Action<object?[]>? native, JsValue? script, JsValue? thisValue, JsRuntime? runtime, QObject? receiver, JsValue? signalKey)
    {
        public JsRuntime? Runtime { get; } = runtime;

        public Action<object?[]>? Native { get; } = native;

        public JsValue? Script { get; } = script;

        public JsValue? This { get; } = thisValue;

        /// <summary>The QML object whose scope created the handler. A destroyed receiver no longer runs.</summary>
        public QObject? Receiver { get; } = receiver;

        /// <summary>The other signal, when this handler is a signal connected to a signal.</summary>
        public JsValue? SignalKey { get; } = signalKey;

        public bool Removed { get; set; }
    }

    private sealed class Disconnect(SignalSlot slot, Handler handler) : IDisposable
    {
        public void Dispose()
        {
            handler.Removed = true;
            slot._handlers.Remove(handler);
        }
    }
}
