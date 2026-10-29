using System.Text.RegularExpressions;
using Jint.Native;
using Winshell.Qml;

namespace Winshell.Runtime;

/// <summary>Creates the objects of a component from its parsed QML and applies their members.</summary>
internal sealed partial class ObjectBuilder(QmlEngine engine, CreationBatch batch)
{
    private readonly JsRuntime _js = engine.Js;

    public QObject Build(QmlObjectNode node, ComponentContext context, QmlDocument doc, QObject? owner)
    {
        var imports = context.Imports ?? engine.ImportsOf(doc);
        var type = imports.ResolveType(node.TypeName)
            ?? throw new QmlCompileException($"{node.TypeName} is not a type", node.Location);

        QObject obj;
        switch (type)
        {
            case NativeTypeRef { Entry.ClrType: var t } when t == typeof(QmlComponent):
                return BuildExplicitComponent(node, context, doc, owner);
            case NativeTypeRef native:
                if (!native.Entry.Creatable)
                {
                    throw new QmlCompileException(
                        native.Entry.UncreatableReason ?? $"Type {native.Entry.Name} is not creatable", node.Location);
                }
                obj = (QObject)Activator.CreateInstance(native.Entry.ClrType)!;
                obj.Engine = engine;
                obj.CreationContext = context;
                break;
            case FileTypeRef file:
                {
                    engine.BeginFileInstance(file.Path, node.Location);
                    try
                    {
                        var fileDoc = engine.LoadDocument(file.Path);
                        if (fileDoc.IsSingleton)
                            throw new QmlCompileException($"{file.Name} is a singleton and cannot be created", node.Location);
                        var fileContext = new ComponentContext(null, engine.ImportsOf(fileDoc));
                        obj = Build(fileDoc.Root, fileContext, fileDoc, owner);
                    }
                    finally
                    {
                        engine.EndFileInstance(file.Path);
                    }
                    break;
                }
            case InlineTypeRef inline:
                {
                    var inlineContext = new ComponentContext(inline.Document == doc ? context : null, engine.ImportsOf(inline.Document));
                    obj = Build(inline.Component.Object, inlineContext, inline.Document, owner);
                    break;
                }
            default:
                throw new QmlCompileException($"{node.TypeName} is not a type", node.Location);
        }

        if (type is NativeTypeRef)
        {
            obj.SetOwner(owner);
            batch.Objects.Add(obj);
        }
        context.ContextObject ??= obj;
        Apply(obj, node, context, doc);
        return obj;
    }

    private QmlComponent BuildExplicitComponent(QmlObjectNode node, ComponentContext context, QmlDocument doc, QObject? owner)
    {
        var content = node.Members.OfType<QmlChildObject>().SingleOrDefault()?.Object
            ?? throw new QmlCompileException("A Component must contain exactly one object", node.Location);
        var component = new QmlComponent(engine, content, context, doc) { Engine = engine };
        component.SetOwner(owner);
        if (node.Id is { } id)
            context.SetId(id, component);
        return component;
    }

    private void Apply(QObject obj, QmlObjectNode node, ComponentContext context, QmlDocument doc)
    {
        foreach (var member in node.Members)
        {
            switch (member)
            {
                case QmlIdMember id:
                    context.SetId(id.Id, obj);
                    break;
                case QmlPropertyDeclaration p:
                    var dynamicProperty = obj.AddDynamicProperty(new DynamicProperty(obj, p.Name, p.Type)
                    {
                        IsReadonly = p.IsReadonly,
                        IsRequired = p.IsRequired,
                        IsDefault = p.IsDefault,
                    });
                    if (p.IsAlias)
                        MakeAlias(obj, dynamicProperty, p, context);
                    if (p.IsRequired)
                        batch.Required.Add(dynamicProperty);
                    break;
                case QmlSignalDeclaration s:
                    obj.AddSignal(new SignalDefinition(s.Name, s.Parameters.Select(x => x.Name).ToList()));
                    break;
                case QmlFunctionDeclaration f:
                    {
                        var parameters = f.Parameters.Select(x => x.Name).ToList();
                        var scope = new ScopeObject(_js, obj, context);
                        var function = _js.Compile(f.Body, parameters, scope, f.BodyLocation.ToString());
                        obj.AddFunction(f.Name, function);
                        obj.AddFunctionDeclaration(f);
                        break;
                    }
            }
        }

        foreach (var member in node.Members)
        {
            if (member is QmlRequiredMember required)
                MarkRequired(obj, required.Name, required.Location);
        }

        foreach (var member in node.Members)
        {
            switch (member)
            {
                case QmlPropertyDeclaration { Value: { } value, IsAlias: false } p:
                    AssignValue(obj, obj, p.Name, value, context, doc, p.Location);
                    break;
                case QmlChildObject child:
                    AppendDefault(obj, child.Object, context, doc, node);
                    break;
                case QmlAssignment a:
                    AssignPath(obj, a.Name, a.Value, context, doc, a.Location);
                    break;
                case QmlGroupedBlock g:
                    ApplyGroup(obj, obj, g, context, doc);
                    break;
                case QmlOnAssignment on:
                    ApplyOn(obj, on, context, doc);
                    break;
            }
        }
    }

    private void MarkRequired(QObject obj, string name, SourceLocation location)
    {
        if (obj.GetDynamicProperty(name) is { } property)
        {
            property.IsRequired = true;
            if (!batch.Required.Contains(property))
                batch.Required.Add(property);
            return;
        }
        // A built-in property has no assignment flag. Unknown names are the typo this syntax used to hide.
        if (!obj.HasProperty(name))
            throw new QmlCompileException($"Required property {name} was not found", location);
    }

    private void ApplyGroup(QObject scopeObject, QObject target, QmlGroupedBlock group, ComponentContext context, QmlDocument doc)
    {
        var groupObject = ResolveSegment(target, group.Name, context, group.Location);
        foreach (var member in group.Members)
        {
            switch (member)
            {
                case QmlAssignment a:
                    AssignPath(scopeObject, a.Name, a.Value, context, doc, a.Location, groupObject);
                    break;
                case QmlGroupedBlock g:
                    ApplyGroup(scopeObject, groupObject, g, context, doc);
                    break;
            }
        }
    }

    /// <summary>The grouped property object or attached object a path segment refers to.</summary>
    private QObject ResolveSegment(QObject target, string path, ComponentContext context, SourceLocation location)
    {
        var current = target;
        foreach (var segment in path.Split('.'))
        {
            if (char.IsUpper(segment[0]))
            {
                var type = context.Imports?.ResolveType(segment) as NativeTypeRef;
                if (type?.Entry.Attached is null)
                    throw new QmlCompileException($"Non-existent attached object \"{segment}\"", location);
                current = current.GetAttached(type.Entry.AttachedType ?? type.Entry.ClrType, type.Entry.Attached);
                continue;
            }
            if (!current.HasProperty(segment))
                throw new QmlCompileException($"Cannot assign to non-existent property \"{segment}\"", location);
            object? value;
            using (DependencyTracker.Suspend())
                value = current.ReadProperty(segment);
            current = value as QObject
                ?? throw new QmlCompileException($"Property \"{segment}\" is not a grouped property", location);
        }
        return current;
    }

    private void AssignPath(QObject scopeObject, string path, QmlValue value, ComponentContext context, QmlDocument doc, SourceLocation location, QObject? baseTarget = null)
    {
        var target = baseTarget ?? scopeObject;
        var dot = path.LastIndexOf('.');
        var name = path;
        // PropertyChanges { label.width: 50 } changes the object with that id.
        if (dot > 0 && target is Quick.PropertyChanges changes && context.FindId(path[..dot]) is { } changed)
        {
            AssignValue(scopeObject, changes.HolderFor(changed), path[(dot + 1)..], value, context, doc, location);
            return;
        }
        if (dot > 0)
        {
            target = ResolveSegment(target, path[..dot], context, location);
            name = path[(dot + 1)..];
        }

        if (Names.SignalOfHandler(name) is { } signalName && !target.HasProperty(name) && target is not Quick.IAcceptsAnyProperty)
        {
            var slot = target.GetSignal(signalName) ?? target.GetSignal(name[2..]);
            if (slot is null && target is QtQml.Connections connections && value is QmlScriptValue handler)
            {
                var handlerScope = new ScopeObject(_js, scopeObject, context);
                var handlerFunction = _js.Compile(ScriptCompiler.HandlerBody(handler.Code, handler.IsBlock), [], handlerScope, handler.Location.ToString());
                connections.AddHandler(signalName, handlerFunction);
                return;
            }
            if (slot is null)
                throw new QmlCompileException($"Cannot assign to non-existent property \"{name}\"", location);
            ConnectHandler(scopeObject, slot, value, context, location);
            return;
        }

        AssignValue(scopeObject, target, name, value, context, doc, location);
    }

    private void ConnectHandler(QObject scopeObject, SignalSlot slot, QmlValue value, ComponentContext context, SourceLocation location)
    {
        if (value is not QmlScriptValue script)
            throw new QmlCompileException("A signal handler must be a script", location);
        var body = ScriptCompiler.HandlerBody(script.Code, script.IsBlock);
        var scope = new ScopeObject(_js, scopeObject, context);
        var function = _js.Compile(body, slot.Definition.Parameters, scope, script.Location.ToString());
        slot.ConnectScript(function, _js.Wrap(scopeObject), _js);
    }

    private void AssignValue(QObject scopeObject, QObject target, string name, QmlValue value, ComponentContext context, QmlDocument doc, SourceLocation location)
    {
        if (!target.HasProperty(name) && target is Quick.IAcceptsAnyProperty)
            target.AddDynamicProperty(new DynamicProperty(target, name, "var"));
        if (!target.HasProperty(name))
            throw new QmlCompileException($"Cannot assign to non-existent property \"{name}\"", location);

        switch (value)
        {
            case QmlObjectValue ov:
                if (IsComponentProperty(target, name))
                {
                    target.RemoveBinding(name);
                    target.WritePropertyDirect(name, MakeComponent(ov.Object, context, doc, target));
                }
                else
                {
                    var child = Build(ov.Object, context, doc, target);
                    Write(target, name, child, location);
                }
                break;
            case QmlListValue list:
                {
                    var items = list.Objects
                        .Select(o => IsComponentListProperty(target, name) ? (object)MakeComponent(o, context, doc, target) : Build(o, context, doc, target))
                        .ToList();
                    Write(target, name, items, location);
                    break;
                }
            case QmlScriptValue script:
                if (!script.IsBlock && TryLiteral(script.Code, out var literal))
                {
                    Write(target, name, literal, location);
                    break;
                }
                var body = ScriptCompiler.BindingBody(script.Code, script.IsBlock);
                var scope = new ScopeObject(_js, scopeObject, context);
                var function = _js.Compile(body, [], scope, script.Location.ToString());
                var thisValue = _js.Wrap(scopeObject);
                var binding = new Binding(target, name, () => _js.Engine.Call(function, thisValue, []), location.ToString());
                target.SetBinding(name, binding);
                batch.Bindings.Add(binding);
                break;
        }
    }

    /// <summary>
    /// Writes a value a document gives a property. It replaces a binding the property's component declared, as in Qt:
    /// <c>Card { opens: false }</c> keeps <c>false</c> even when Card.qml binds <c>opens</c> to something that changes.
    /// </summary>
    private void Write(QObject target, string name, object? value, SourceLocation location)
    {
        target.RemoveBinding(name);
        try
        {
            target.WriteProperty(name, value);
        }
        catch (QmlException e)
        {
            engine.Log.Warn("scene", $"{location}: {e.Message}");
        }
    }

    /// <summary>
    /// Adds a child object to the default property. A default property the same object declares is for the documents
    /// that use it: the object's own children go to the default property it had before, as in Qt, so
    /// <c>default property alias content: row.data</c> does not take the children that make up <c>row</c>.
    /// </summary>
    private void AppendDefault(QObject obj, QmlObjectNode childNode, ComponentContext context, QmlDocument doc, QmlObjectNode declaredBy)
    {
        var own = declaredBy.Members.OfType<QmlPropertyDeclaration>().Where(p => p.IsDefault).Select(p => p.Name).ToHashSet();
        var defaultName = obj.DynamicProperties.FirstOrDefault(p => p.IsDefault && !own.Contains(p.Name))?.Name ?? obj.Meta.DefaultProperty
            ?? throw new QmlCompileException($"Cannot assign to non-existent default property of {obj.TypeName}", childNode.Location);

        if (IsComponentProperty(obj, defaultName))
        {
            obj.WritePropertyDirect(defaultName, MakeComponent(childNode, context, doc, obj));
            return;
        }
        var child = Build(childNode, context, doc, obj);
        object? current;
        using (DependencyTracker.Suspend())
            current = obj.ReadProperty(defaultName);
        if (current is IQmlList list)
        {
            try
            {
                list.Append(child);
            }
            catch (QmlException e)
            {
                engine.Log.Warn("scene", $"{childNode.Location}: {e.Message}");
            }
        }
        else
        {
            Write(obj, defaultName, child, childNode.Location);
        }
    }

    private QmlComponent MakeComponent(QmlObjectNode node, ComponentContext context, QmlDocument doc, QObject owner)
    {
        // An explicit "Component { ... }" in a component property is that component.
        if (context.Imports?.ResolveType(node.TypeName) is NativeTypeRef { Entry.ClrType: var t } && t == typeof(QmlComponent))
            return BuildExplicitComponent(node, context, doc, owner);
        var component = new QmlComponent(engine, node, context, doc) { Engine = engine };
        component.SetOwner(owner);
        return component;
    }

    private static bool IsComponentProperty(QObject obj, string name)
    {
        if (obj.GetDynamicProperty(name) is { } p)
            return p.QmlType == "Component";
        return obj.PropertyClrType(name) == typeof(QmlComponent);
    }

    private static bool IsComponentListProperty(QObject obj, string name)
    {
        if (obj.GetDynamicProperty(name) is { } p)
            return p.QmlType == "list<Component>";
        using (DependencyTracker.Suspend())
            return obj.HasProperty(name) && obj.ReadProperty(name) is IQmlList { ElementType: var t } && t == typeof(QmlComponent);
    }

    private void ApplyOn(QObject obj, QmlOnAssignment on, ComponentContext context, QmlDocument doc)
    {
        var dot = on.Property.LastIndexOf('.');
        var target = dot > 0 ? ResolveSegment(obj, on.Property[..dot], context, on.Location) : obj;
        var name = dot > 0 ? on.Property[(dot + 1)..] : on.Property;
        var source = Build(on.Object, context, doc, obj);
        if (source is IPropertyValueSource valueSource)
            valueSource.SetTarget(target, name);
        else
            throw new QmlCompileException($"{source.TypeName} cannot be used with \"on\"", on.Location);
    }

    private void MakeAlias(QObject owner, DynamicProperty property, QmlPropertyDeclaration declaration, ComponentContext context)
    {
        if (declaration.Value is not QmlScriptValue { IsBlock: false } script)
            throw new QmlCompileException("Invalid alias reference", declaration.Location);
        var parts = script.Code.Trim().Split('.');
        (QObject, string?) Resolve()
        {
            var obj = context.FindId(parts[0])
                ?? throw new QmlException($"Invalid alias reference. Unable to find id \"{parts[0]}\"");
            for (var i = 1; i < parts.Length - 1; i++)
            {
                using (DependencyTracker.Suspend())
                    obj = obj.ReadProperty(parts[i]) as QObject ?? throw new QmlException($"Invalid alias target \"{script.Code}\"");
            }
            return (obj, parts.Length > 1 ? parts[^1] : null);
        }
        property.MakeAlias(Resolve);
        // The forwarder has to leave with the alias. A long-lived target would otherwise keep one observer per instance.
        var watch = new AliasWatch(owner, property.Name, parts, context);
        batch.AfterBindings.Add(watch.Attach);
    }

    public void SetInitialProperties(QObject root, ComponentContext context, IReadOnlyDictionary<string, object?> properties)
    {
        foreach (var (name, value) in properties)
        {
            if (root.HasProperty(name))
            {
                try
                {
                    root.RemoveBinding(name);
                    root.WriteProperty(name, value);
                }
                catch (QmlException e)
                {
                    engine.Log.Warn("scene", $"{root.TypeName}: {e.Message}");
                }
            }
            else
            {
                context.SetContextProperty(name, value);
            }
        }
    }

    private static bool TryLiteral(string code, out object? value)
    {
        var s = code.Trim();
        value = null;
        if (s.Length == 0)
            return false;
        if (s is "true" or "false")
        {
            value = s == "true";
            return true;
        }
        if (NumberLiteral().IsMatch(s))
        {
            value = double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0] && s.IndexOf('\\') < 0 && s.IndexOf(s[0], 1) == s.Length - 1)
        {
            value = s[1..^1];
            return true;
        }
        return false;
    }

    [GeneratedRegex(@"^-?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$")]
    private static partial Regex NumberLiteral();

    /// <summary>
    /// Forwards changes along an alias path, and drops those watches when the alias owner is destroyed or an
    /// intermediate object is replaced.
    /// </summary>
    private sealed class AliasWatch(QObject owner, string name, string[] parts, ComponentContext context) : IPropertyObserver
    {
        private readonly List<(QObject Object, string Property)> _watched = [];
        private bool _hooked;

        public void Attach()
        {
            Detach();
            if (owner.IsDestroyed || owner.IsDestroying || parts.Length < 2)
                return;
            var current = context.FindId(parts[0]);
            if (current is null)
                return;
            for (var i = 1; i < parts.Length; i++)
            {
                var property = parts[i];
                current.AddObserver(property, this);
                _watched.Add((current, property));
                if (i == parts.Length - 1)
                    break;
                using (DependencyTracker.Suspend())
                    current = current.ReadProperty(property) as QObject;
                if (current is null)
                    break;
            }
            if (!_hooked)
            {
                _hooked = true;
                owner.GetSignal("destroyed")?.Connect(_ => Detach());
            }
        }

        public void OnPropertyChanged(QObject sender, string property)
        {
            if (owner.IsDestroyed || owner.IsDestroying)
            {
                Detach();
                return;
            }
            var final = _watched.Count > 0
                && ReferenceEquals(_watched[^1].Object, sender)
                && _watched[^1].Property == property
                && property == parts[^1];
            if (!final)
                Attach();
            owner.NotifyChanged(name);
        }

        private void Detach()
        {
            foreach (var (obj, property) in _watched)
                obj.RemoveObserver(property, this);
            _watched.Clear();
        }
    }
}

/// <summary>An object used with <c>on</c>: <c>Behavior on x { }</c> or <c>NumberAnimation on x { }</c>.</summary>
public interface IPropertyValueSource
{
    void SetTarget(QObject target, string property);
}
