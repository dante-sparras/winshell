using Jint;
using Winshell.Qml;

namespace Winshell.Runtime;

/// <summary>
/// QML's <c>Component</c>: a piece of QML that can be created any number of times. Delegates of Repeater
/// and Variants are components.
/// </summary>
public sealed class QmlComponent : QObject
{
    private readonly QmlEngine _engine;
    private readonly QmlObjectNode? _node;
    private readonly ComponentContext? _context;
    private readonly QmlDocument? _document;
    private readonly string? _errorString;

    public QmlComponent()
    {
        _engine = null!;
    }

    internal QmlComponent(QmlEngine engine, QmlObjectNode node, ComponentContext context, QmlDocument document)
    {
        _engine = engine;
        _node = node;
        _context = context;
        _document = document;
        Engine = engine;
    }

    /// <summary>A component for a whole file, as <c>Qt.createComponent()</c> makes.</summary>
    internal QmlComponent(QmlEngine engine, QmlDocument document)
    {
        _engine = engine;
        _document = document;
        _node = document.Root;
        Engine = engine;
        IsFile = true;
    }

    internal QmlComponent(QmlEngine engine, string error)
    {
        _engine = engine;
        _errorString = error;
        Engine = engine;
    }

    public enum ComponentStatus
    {
        Null,
        Ready,
        Loading,
        Error,
    }

    [QmlEnum]
    public enum StatusValue
    {
        Null = 0,
        Ready = 1,
        Loading = 2,
        Error = 3,
    }

    private bool IsFile { get; }

    [Qml]
    public int Status => _node is null ? (int)StatusValue.Error : (int)StatusValue.Ready;

    [Qml]
    public string Url => _document?.File ?? "";

    [Qml]
    public string ErrorString() => _errorString ?? "";

    public QmlObjectNode? Node => _node;

    /// <summary>Creates an instance. <paramref name="parent"/> becomes its visual parent when both are items.</summary>
    [Qml]
    public QObject? CreateObject(QObject? parent = null, object? properties = null)
    {
        var props = ValueConverter.Unwrap(properties) switch
        {
            null => null,
            Jint.Native.JsValue js when js.IsObject() => ToDictionary(js),
            IReadOnlyDictionary<string, object?> d => d,
            _ => null,
        };
        try
        {
            return Create(parent, props, null);
        }
        catch (Exception e) when (e is QmlCompileException or QmlException)
        {
            _engine.Log.Warn("scene", e.Message);
            return null;
        }
    }

    private static Dictionary<string, object?> ToDictionary(Jint.Native.JsValue js)
    {
        var obj = js.AsObject();
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in obj.GetOwnPropertyKeys())
        {
            if (key.IsString())
                dict[key.AsString()] = obj.Get(key);
        }
        return dict;
    }

    /// <summary>
    /// Creates an instance with the given initial properties. <paramref name="contextProperties"/> become
    /// names the instance can see, such as a delegate's <c>index</c>.
    /// </summary>
    public QObject Create(QObject? parent, IReadOnlyDictionary<string, object?>? properties, IReadOnlyDictionary<string, object?>? contextProperties, Action<QObject>? beforeCompletion = null)
    {
        if (_node is null || _document is null)
            throw new QmlException(_errorString ?? "Cannot create an object from an empty component");
        var context = IsFile
            ? new ComponentContext(null, _engine.ImportsOf(_document))
            : new ComponentContext(_context, _context!.Imports ?? _engine.ImportsOf(_document));
        if (contextProperties is not null)
        {
            foreach (var (k, v) in contextProperties)
                context.SetContextProperty(k, v);
        }
        return _engine.Create(_node, context, _document, parent, properties, isFileRoot: IsFile, obj =>
        {
            if (parent is not null)
                QmlParenting.SetParent(obj, parent);
            beforeCompletion?.Invoke(obj);
        });
    }
}

/// <summary>Lets the visual layer decide what a parent means for a created object.</summary>
public static class QmlParenting
{
    /// <summary>Set by the visual layer: makes an item the visual child of another.</summary>
    public static Func<QObject, QObject, bool>? VisualParent { get; set; }

    public static void SetParent(QObject child, QObject parent)
    {
        if (VisualParent?.Invoke(child, parent) == true)
            return;
        child.SetOwner(parent);
    }
}

/// <summary>The attached <c>Component</c> object: <c>Component.onCompleted</c> and <c>Component.onDestruction</c>.</summary>
[QmlSignal("completed")]
[QmlSignal("destruction")]
public sealed class ComponentAttached : QObject, IDestructionListener
{
    public void OnDestroying() => Emit("destruction");
}
