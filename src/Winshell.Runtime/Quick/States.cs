using Jint.Native;

namespace Winshell.Runtime.Quick;

/// <summary>QtQuick's <c>State</c>: a named set of property changes.</summary>
[QmlDefaultProperty("changes")]
public sealed class State : QObject
{
    private string _name = "";
    private bool _when;
    private bool _whenSet;

    public State()
    {
        Changes = new QmlList<QObject>(this, "changes", added: c => c.SetOwner(this));
    }

    [Qml]
    public string Name { get { Track(); return _name; } set => SetField(ref _name, value ?? ""); }

    [Qml]
    public bool When
    {
        get { Track(); return _when; }
        set
        {
            _whenSet = true;
            if (SetField(ref _when, value))
                (Owner as Item)?.Let(States.Reevaluate);
        }
    }

    public bool HasWhen => _whenSet;

    [Qml]
    public string Extend { get; set; } = "";

    [Qml]
    public QmlList<QObject> Changes { get; }
}

/// <summary>QtQuick's <c>PropertyChanges</c>: properties set on a target while a state is active.</summary>
public sealed class PropertyChanges : QObject, IAcceptsAnyProperty
{
    [Qml]
    public QObject? Target { get; set; }

    [Qml]
    public bool RestoreEntryValues { get; set; } = true;

    [Qml]
    public bool ExplicitChanges { get; set; }

    /// <summary>The property assignments: every QML property declared or set on this object except target.</summary>
    internal IEnumerable<(string Name, object? Value)> Values() =>
        DynamicProperties.Where(p => p.Name != "target").Select(p => (p.Name, p.Read()));
}

/// <summary>QtQuick's <c>Transition</c>. Its animations run when the state changes between from and to.</summary>
[QmlDefaultProperty("animations")]
public sealed class Transition : QObject
{
    public Transition()
    {
        Animations = new QmlList<AnimationBase>(this, "animations", added: a => a.SetOwner(this));
    }

    [Qml] public string From { get; set; } = "*";

    [Qml] public string To { get; set; } = "*";

    [Qml] public bool Reversible { get; set; }

    [Qml] public bool Enabled { get; set; } = true;

    [Qml] public bool Running => Animations.Snapshot.Any(a => a.Running);

    [Qml]
    public QmlList<AnimationBase> Animations { get; }
}

public static class States
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item, Dictionary<(QObject, string), object?>> Saved = new();

    public static void Let(this Item item, Action<Item> action) => action(item);

    /// <summary>Picks the state whose <c>when</c> holds, for items that use when instead of setting state.</summary>
    public static void Reevaluate(Item item)
    {
        var active = item.StatesList.Snapshot.OfType<State>().FirstOrDefault(s => s.HasWhen && s.When);
        if (active is not null)
            item.State = active.Name;
        else if (item.StatesList.Snapshot.OfType<State>().Any(s => s.HasWhen && s.Name == item.State))
            item.State = "";
    }

    public static void Apply(Item item, string name)
    {
        if (!item.IsComplete)
            return;
        var saved = Saved.GetOrCreateValue(item);
        var oldState = saved.Count > 0 ? "?" : "";
        // Restore what the last state changed, then apply the new one.
        foreach (var ((target, property), value) in saved)
        {
            if (!target.IsDestroyed)
                Write(target, property, value);
        }
        saved.Clear();
        var state = item.StatesList.Snapshot.OfType<State>().FirstOrDefault(s => s.Name == name);
        if (state is null)
            return;
        var transition = item.TransitionsList.Snapshot.OfType<Transition>().FirstOrDefault(t => t.Enabled && (t.To == "*" || t.To == name));
        foreach (var change in state.Changes.Snapshot.OfType<PropertyChanges>())
        {
            var target = change.Target ?? item;
            foreach (var (prop, value) in change.Values())
            {
                if (!target.HasProperty(prop))
                    continue;
                using (DependencyTracker.Suspend())
                    saved[(target, prop)] = target.ReadProperty(prop);
                if (transition is not null)
                {
                    foreach (var a in transition.Animations.Snapshot)
                    {
                        if (a is PropertyAnimation pa)
                        {
                            pa.OverrideTrack = (target, prop);
                            pa.OverrideTo = value;
                            pa.Start();
                        }
                        else if (a is AnimationGroup g)
                        {
                            g.Override(target, prop, value, null);
                            g.Start();
                        }
                    }
                    if (transition.Animations.Count > 0)
                        continue;
                }
                Write(target, prop, value);
            }
        }
        _ = oldState;
    }

    private static void Write(QObject target, string property, object? value)
    {
        try
        {
            target.WritePropertyDirect(property, value is JsValue js ? js : value);
        }
        catch (QmlException)
        {
        }
    }
}
