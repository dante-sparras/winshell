using Jint.Native;

namespace Winshell.Runtime;

/// <summary>The handlers connected to one signal of one object.</summary>
public sealed class SignalSlot(QObject owner, SignalDefinition definition)
{
    private readonly List<Handler> _handlers = [];

    public QObject Owner { get; } = owner;

    public SignalDefinition Definition { get; } = definition;

    public int Count => _handlers.Count;

    public IDisposable Connect(Action<object?[]> handler)
    {
        var h = new Handler(handler, null, null);
        _handlers.Add(h);
        return new Disconnect(this, h);
    }

    /// <summary>Connects a JavaScript function. <paramref name="thisValue"/> is its <c>this</c>.</summary>
    public IDisposable ConnectScript(JsValue function, JsValue? thisValue)
    {
        var h = new Handler(null, function, thisValue);
        _handlers.Add(h);
        return new Disconnect(this, h);
    }

    public bool DisconnectScript(JsValue function)
    {
        var index = _handlers.FindIndex(h => h.Script is not null && ReferenceEquals(h.Script, function));
        if (index < 0)
            return false;
        _handlers.RemoveAt(index);
        return true;
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
            try
            {
                if (h.Native is not null)
                {
                    h.Native(args);
                }
                else if (Owner.Engine is { } engine)
                {
                    engine.Js.CallFunction(h.Script!, h.This, args);
                }
            }
            catch (Exception e)
            {
                (Owner.Engine?.Log ?? Log.Default).ScriptError(e, $"{Owner.TypeName}.{Definition.Name}");
            }
        }
    }

    public void Clear()
    {
        foreach (var h in _handlers)
            h.Removed = true;
        _handlers.Clear();
    }

    private sealed class Handler(Action<object?[]>? native, JsValue? script, JsValue? thisValue)
    {
        public Action<object?[]>? Native { get; } = native;

        public JsValue? Script { get; } = script;

        public JsValue? This { get; } = thisValue;

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
