using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Winshell.Runtime;

/// <summary>How JavaScript sees a <see cref="QObject"/>: its QML properties, methods, functions and signals.</summary>
public sealed class QObjectWrapper : ObjectInstance
{
    private readonly JsRuntime _runtime;
    private Dictionary<string, JsValue>? _members;

    internal QObjectWrapper(JsRuntime runtime, QObject target)
        : base(runtime.Engine)
    {
        _runtime = runtime;
        Target = target;
    }

    protected override ObjectInstance GetPrototypeOf() => _runtime.Engine.Intrinsics.Object.PrototypeObject;

    public QObject Target { get; }

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (!property.IsString())
            return base.Get(property, receiver);
        var name = property.AsString();
        if (Target.IsDestroyed)
            return name == "toString" ? MemberFunction(name) : Undefined;
        if (TryGetMember(name, out var value))
            return value;
        return base.Get(property, receiver);
    }

    public bool TryGetMember(string name, out JsValue value)
    {
        if (Target.HasProperty(name))
        {
            value = _runtime.ToJs(Target.ReadProperty(name));
            return true;
        }
        if (Target.GetFunction(name) is { } function)
        {
            value = function;
            return true;
        }
        if (Target.Meta.Methods.ContainsKey(name) || name == "toString")
        {
            value = MemberFunction(name);
            return true;
        }
        if (Target.GetSignal(name) is { } slot)
        {
            value = Member(name, () => SignalFunction.Create(_runtime, slot));
            return true;
        }
        value = Undefined;
        return false;
    }

    private JsValue MemberFunction(string name) => Member(name, () =>
    {
        if (name == "toString")
            return new ClrFunction(Engine, "toString", (_, _) => Target.ToString());
        var method = Target.Meta.Methods[name];
        return new ClrFunction(Engine, name, (_, args) =>
        {
            var converted = args.Select(a => (object?)a).ToArray();
            return _runtime.ToJs(method.Invoke(Target, converted));
        });
    });

    private JsValue Member(string name, Func<JsValue> create)
    {
        _members ??= [];
        if (!_members.TryGetValue(name, out var value))
            _members[name] = value = create();
        return value;
    }

    public override bool Set(JsValue property, JsValue value, JsValue receiver)
    {
        if (!property.IsString())
            return base.Set(property, value, receiver);
        var name = property.AsString();
        if (Target.IsDestroyed)
            return false;
        if (Target.HasProperty(name))
        {
            _runtime.AssignProperty(Target, name, value);
            return true;
        }
        throw new JavaScriptException(Engine.Intrinsics.TypeError, $"Cannot assign to non-existent property \"{name}\"");
    }

    public override bool HasProperty(JsValue property)
    {
        if (property.IsString() && !Target.IsDestroyed && Target.HasMember(property.AsString()))
            return true;
        return base.HasProperty(property);
    }

    public override PropertyDescriptor GetOwnProperty(JsValue property)
    {
        if (property.IsString() && !Target.IsDestroyed)
        {
            var name = property.AsString();
            if (Target.HasProperty(name) && TryGetMember(name, out var value))
                return new PropertyDescriptor(value, writable: true, enumerable: true, configurable: false);
        }
        return base.GetOwnProperty(property);
    }

    public override List<JsValue> GetOwnPropertyKeys(Types types = Types.Empty | Types.String | Types.Symbol)
    {
        var keys = new List<JsValue>();
        if ((types & Types.String) != 0 && !Target.IsDestroyed)
        {
            foreach (var name in Target.Meta.Properties.Keys)
                keys.Add(name);
            foreach (var p in Target.DynamicProperties)
                keys.Add(p.Name);
        }
        keys.AddRange(base.GetOwnPropertyKeys(types));
        return keys;
    }

    public override object ToObject() => Target;

    public override string ToString() => Target.ToString();
}

/// <summary>A signal as a JavaScript value: calling it emits, and it has <c>connect</c> and <c>disconnect</c>.</summary>
public static class SignalFunction
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsValue, SignalSlot> Slots = new();

    public static JsValue Create(JsRuntime runtime, SignalSlot slot)
    {
        var engine = runtime.Engine;
        var function = new ClrFunction(engine, slot.Definition.Name, (_, args) =>
        {
            slot.Owner.Emit(slot.Definition.Name, args.Select(a => (object?)a).ToArray());
            return JsValue.Undefined;
        });
        Slots.Add(function, slot);
        function.Set("connect", new ClrFunction(engine, "connect", (_, args) =>
        {
            if (args.Length == 0)
                return JsValue.Undefined;
            // connect(object, method) form, or connect(function).
            var target = args.Length >= 2 && args[1].IsCallable() ? args[1] : args[0];
            var thisValue = args.Length >= 2 ? args[0] : null;
            if (Slots.TryGetValue(target, out var other))
            {
                slot.Connect(a => other.Owner.Emit(other.Definition.Name, a));
                return JsValue.Undefined;
            }
            slot.ConnectScript(target, thisValue);
            return JsValue.Undefined;
        }));
        function.Set("disconnect", new ClrFunction(engine, "disconnect", (_, args) =>
        {
            if (args.Length > 0)
                slot.DisconnectScript(args.Length >= 2 ? args[1] : args[0]);
            return JsValue.Undefined;
        }));
        return function;
    }
}
