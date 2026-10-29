using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Winshell.Runtime;

/// <summary>A function made by <c>Qt.binding()</c>. Assigning it to a property makes a binding.</summary>
public sealed class BindingFunction(JsValue function)
{
    public JsValue Function { get; } = function;
}

/// <summary>The JavaScript engine of one <see cref="QmlEngine"/>, and the conversions between its values and C#.</summary>
public sealed class JsRuntime
{
    private readonly QmlEngine _qml;
    private readonly ConcurrentDictionary<string, JsValue> _factories = new(StringComparer.Ordinal);

    /// <summary>How long one script entry may run before it is stopped. A test can pass a shorter budget.</summary>
    internal static readonly TimeSpan DefaultScriptBudget = TimeSpan.FromSeconds(2);

    public JsRuntime(QmlEngine qml, TimeSpan? scriptBudget = null)
    {
        _qml = qml;
        var budget = scriptBudget ?? DefaultScriptBudget;
        Engine = new Engine(options =>
        {
            options.Strict(false);
            // Syntax and compile failures, and a file the account cannot read, have to reach a script's try/catch.
            // QmlSyntaxException and QmlCompileException do not derive from QmlException.
            options.CatchClrExceptions(e => e is QmlException or global::Winshell.Qml.QmlSyntaxException or QmlCompileException
                or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException);
            options.SetTypeResolver(new TypeResolver { MemberNameComparer = StringComparer.OrdinalIgnoreCase });
            options.Interop.AllowGetType = false;
            options.Interop.AllowSystemReflection = false;
            options.LimitRecursion(512);
            // A binding, signal or callLater that never returns would freeze the shell. Two seconds is long
            // enough for a real handler and short enough to get the taskbar back. The same bound covers a regex.
            options.TimeoutInterval(budget);
            options.Constraints.RegexTimeout = budget;
        });
        Globals.Install(this, qml);
    }

    public Engine Engine { get; }

    public QmlEngine Qml => _qml;

    /// <summary>How many pieces of script were compiled. Code compiles once per place it is written.</summary>
    public int CompiledFunctionCount => _factories.Count;

    /// <summary>Converts a C# value to JavaScript.</summary>
    public JsValue ToJs(object? value)
    {
        switch (value)
        {
            case null:
                return JsValue.Null;
            case JsValue js:
                return js;
            case string s:
                return s;
            case bool b:
                return b;
            case double d:
                return d;
            case int i:
                return i;
            case long l:
                return l;
            case float f:
                return f;
            case Enum e:
                return ValueConverter.EnumToInt(e);
            case QObject o:
                return Wrap(o);
            case DateTime dt:
                return JsValue.FromObject(Engine, dt);
            case TimeSpan ts:
                return ts.TotalMilliseconds;
            case IQmlList list:
                return new JsArray(Engine, list.ToList().Select(ToJs).ToArray());
            case IDictionary<string, object?> dict:
                {
                    var obj = new JsObject(Engine);
                    foreach (var (k, v) in dict)
                        obj.Set(k, ToJs(v));
                    return obj;
                }
            case string[] strings:
                return new JsArray(Engine, strings.Select(s => (JsValue)s).ToArray());
            case IEnumerable items when value is not QColor:
                return new JsArray(Engine, items.Cast<object?>().Select(ToJs).ToArray());
            default:
                return JsValue.FromObject(Engine, value);
        }
    }

    public JsValue Wrap(QObject o)
    {
        // An object can outlive the engine that first wrapped it, such as a screen; each engine has its own wrapper.
        if (o.Wrapper is QObjectWrapper existing && existing.Runtime == this)
            return existing;
        var wrapper = new QObjectWrapper(this, o);
        o.Wrapper = wrapper;
        return wrapper;
    }

    [ThreadStatic]
    private static Stack<QObject?>? _scopeStack;

    /// <summary>The QML object whose script is running, so a <c>connect</c> can die with that object.</summary>
    internal QObject? CurrentScope => _scopeStack is { Count: > 0 } ? _scopeStack.Peek() : null;

    /// <summary>Pushes <paramref name="scope"/> until the returned value is disposed.</summary>
    internal IDisposable EnterScope(QObject? scope)
    {
        (_scopeStack ??= new Stack<QObject?>()).Push(scope);
        return new ScopeFrame();
    }

    private sealed class ScopeFrame : IDisposable
    {
        public void Dispose()
        {
            if (_scopeStack is { Count: > 0 })
                _scopeStack.Pop();
        }
    }

    public JsValue CallFunction(JsValue function, JsValue? thisValue, object?[] args)
    {
        var jsArgs = new JsValue[args.Length];
        for (var i = 0; i < args.Length; i++)
            jsArgs[i] = ToJs(args[i]);
        var scope = thisValue is QObjectWrapper wrapper ? wrapper.Target : CurrentScope;
        using var _ = EnterScope(scope);
        return Engine.Call(function, thisValue ?? JsValue.Undefined, jsArgs);
    }

    /// <summary>A script assignment to a property: a <c>Qt.binding()</c> function makes a binding, anything else removes it.</summary>
    public void AssignProperty(QObject target, string name, JsValue value)
    {
        if (value is ObjectWrapper { Target: BindingFunction bindingFunction })
        {
            var binding = new Binding(target, name, () => Engine.Call(bindingFunction.Function, Wrap(target), []), $"Qt.binding for {target.TypeName}.{name}");
            target.SetBinding(name, binding);
            binding.Evaluate();
            return;
        }
        try
        {
            target.AssignFromScript(name, value);
        }
        catch (QmlException e)
        {
            // A script sees a failed assignment as QML reports it, a TypeError where it was written.
            throw new JavaScriptException(Engine.Intrinsics.TypeError, e.Message);
        }
    }

    /// <summary>
    /// Compiles code into a function that runs with QML name lookup: ids, the scope object's members, the
    /// component root's members, then imports and globals. The function is made once per piece of source and
    /// bound to a scope each time. <paramref name="sourceName"/> is where the code starts, <c>file[line:column]</c>,
    /// so an error in it can name its line.
    /// </summary>
    public JsValue Compile(string body, IReadOnlyList<string> parameters, ObjectInstance scope, string sourceName)
    {
        var key = sourceName + "\u0001" + string.Join(",", parameters) + "\u0001" + body;
        var factory = _factories.GetOrAdd(key, _ =>
        {
            var sb = new StringBuilder();
            // The function is made inside the with block, so its own parameters and locals come before the QML
            // names, as in QML, and the QML names before the globals.
            sb.Append("(function(__qmlScope) { with (__qmlScope) { return function(");
            sb.Append(string.Join(", ", parameters));
            sb.Append(") {");
            sb.Append(body);
            sb.Append("\n}; } })");
            return Engine.Evaluate(sb.ToString(), sourceName);
        });
        return Engine.Call(factory, JsValue.Undefined, [scope]);
    }
}
