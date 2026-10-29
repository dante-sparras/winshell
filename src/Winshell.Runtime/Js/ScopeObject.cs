using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace Winshell.Runtime;

/// <summary>
/// The object a binding's <c>with</c> statement looks names up in, in QML's order: ids and context properties
/// of the component, the scope object's members, the component root's members, then the enclosing components,
/// then the file's imports. A name none of them has falls through to the JavaScript globals.
/// </summary>
public sealed class ScopeObject : ObjectInstance
{
    /// <summary>
    /// Names the JavaScript globals provide. A scope that does not have one still has to be walked once, in case a
    /// property uses the name, and the miss is remembered so the next binding does not walk it again.
    /// </summary>
    private static readonly HashSet<string> Intrinsics = new(StringComparer.Ordinal)
    {
        "AggregateError", "Array", "ArrayBuffer", "Atomics", "BigInt", "BigInt64Array", "BigUint64Array", "Boolean",
        "DataView", "Date", "Error", "EvalError", "FinalizationRegistry", "Float32Array", "Float64Array", "Function",
        "Infinity", "Int16Array", "Int32Array", "Int8Array", "Intl", "JSON", "Map", "Math", "NaN", "Number", "Object",
        "Promise", "Proxy", "Qt", "RangeError", "ReferenceError", "Reflect", "RegExp", "Set", "SharedArrayBuffer",
        "String", "Symbol", "SyntaxError", "TypeError", "URIError", "Uint16Array", "Uint32Array", "Uint8Array",
        "Uint8ClampedArray", "WeakMap", "WeakRef", "WeakSet", "console", "decodeURI", "decodeURIComponent",
        "encodeURI", "encodeURIComponent", "escape", "eval", "globalThis", "isFinite", "isNaN", "parseFloat",
        "parseInt", "undefined", "unescape",
    };

    private readonly JsRuntime _runtime;
    private readonly QObject? _scopeObject;
    private readonly ComponentContext _context;
    private HashSet<string>? _absent;

    public ScopeObject(JsRuntime runtime, QObject? scopeObject, ComponentContext context)
        : base(runtime.Engine)
    {
        _runtime = runtime;
        _scopeObject = scopeObject;
        _context = context;
    }

    private bool TryResolve(string name, out JsValue value, out QObject? owner)
    {
        owner = null;
        var first = true;
        for (var ctx = _context; ctx is not null; ctx = ctx.Parent)
        {
            if (ctx.TryGetId(name, out var idObject))
            {
                value = _runtime.Wrap(idObject);
                return true;
            }
            if (ctx.TryGetContextProperty(name, out var contextValue))
            {
                value = _runtime.ToJs(contextValue);
                return true;
            }
            if (first && _scopeObject is not null && !_scopeObject.IsDestroyed && _scopeObject.HasMember(name))
            {
                owner = _scopeObject;
                return ((QObjectWrapper)_runtime.Wrap(_scopeObject)).TryGetMember(name, out value);
            }
            first = false;
            if (ctx.ContextObject is { IsDestroyed: false } root && root.HasMember(name))
            {
                owner = root;
                return ((QObjectWrapper)_runtime.Wrap(root)).TryGetMember(name, out value);
            }
        }
        var imports = _context.Imports;
        if (imports is not null && char.IsUpper(name[0]) && imports.TryResolveName(name, _scopeObject, _context, out value))
            return true;
        value = Undefined;
        return false;
    }

    public override bool HasProperty(JsValue property)
    {
        if (!property.IsString())
            return false;
        var name = property.AsString();
        if (name.Length == 0)
            return false;
        if (_absent is not null && _absent.Contains(name))
            return false;
        if (Declares(name))
            return true;
        if (Intrinsics.Contains(name))
            (_absent ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        return false;
    }

    /// <summary>Whether this scope has the name. This does not read the value, and it does not load a script import.</summary>
    private bool Declares(string name)
    {
        var first = true;
        for (var ctx = _context; ctx is not null; ctx = ctx.Parent)
        {
            if (ctx.TryGetId(name, out _))
                return true;
            if (ctx.HasContextProperty(name))
                return true;
            if (first && _scopeObject is not null && !_scopeObject.IsDestroyed && _scopeObject.HasMember(name))
                return true;
            first = false;
            if (ctx.ContextObject is { IsDestroyed: false } root && root.HasMember(name))
                return true;
        }
        var imports = _context.Imports;
        return imports is not null && name.Length > 0 && char.IsUpper(name[0]) && imports.DeclaresName(name);
    }

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (!property.IsString())
            return Undefined;
        return TryResolve(property.AsString(), out var value, out _) ? value : Undefined;
    }

    public override PropertyDescriptor GetOwnProperty(JsValue property)
    {
        if (property.IsString() && TryResolve(property.AsString(), out var value, out _))
            return new PropertyDescriptor(value, true, true, true);
        return PropertyDescriptor.Undefined;
    }

    public override bool Set(JsValue property, JsValue value, JsValue receiver)
    {
        if (!property.IsString())
            return false;
        var name = property.AsString();
        var first = true;
        for (var ctx = _context; ctx is not null; ctx = ctx.Parent)
        {
            if (ctx.TryGetId(name, out _))
                throw new JavaScriptException(Engine.Intrinsics.TypeError, $"Invalid write to id \"{name}\"");
            if (first && _scopeObject is not null && _scopeObject.HasProperty(name))
            {
                _runtime.AssignProperty(_scopeObject, name, value);
                return true;
            }
            first = false;
            if (ctx.ContextObject is { } root && root.HasProperty(name))
            {
                _runtime.AssignProperty(root, name, value);
                return true;
            }
        }
        return false;
    }

    public override List<JsValue> GetOwnPropertyKeys(Types types = Types.Empty | Types.String | Types.Symbol) => [];
}
