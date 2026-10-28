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

/// <summary>
/// QtQuick's <c>PropertyChanges</c>: properties set on a target while a state is active. They are written as
/// <c>target: box; width: 100</c>, or with the target's id, <c>box.width: 100</c>.
/// </summary>
public sealed class PropertyChanges : QObject, IAcceptsAnyProperty
{
    private List<(QObject Target, QObject Holder)>? _qualified;

    [Qml]
    public QObject? Target { get; set; }

    [Qml]
    public bool RestoreEntryValues { get; set; } = true;

    [Qml]
    public bool ExplicitChanges { get; set; }

    /// <summary>The object that holds the values written as <c>id.property: value</c> for one target.</summary>
    internal QObject HolderFor(QObject target)
    {
        _qualified ??= [];
        foreach (var (t, holder) in _qualified)
        {
            if (t == target)
                return holder;
        }
        var created = new QualifiedValues { Engine = Engine };
        created.SetOwner(this);
        _qualified.Add((target, created));
        return created;
    }

    /// <summary>The property assignments, on <see cref="Target"/> (or <paramref name="item"/>) and on targets named by id.</summary>
    internal IEnumerable<(QObject Target, string Name, object? Value)> Values(QObject item)
    {
        var target = Target ?? item;
        foreach (var p in DynamicProperties)
        {
            if (p.Name != "target")
                yield return (target, p.Name, p.Read());
        }
        foreach (var (qualifiedTarget, holder) in _qualified ?? [])
        {
            foreach (var p in holder.DynamicProperties)
                yield return (qualifiedTarget, p.Name, p.Read());
        }
    }

    private sealed class QualifiedValues : QObject, IAcceptsAnyProperty;
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

    /// <summary>Stops the animations and forgets the changes they ran for, before a state change.</summary>
    internal void Reset() => Reset(Animations.Snapshot);

    /// <summary>Gives a change to the animations that animate it. Returns whether any does.</summary>
    internal bool Animate(QObject target, string property, object? to) => Animate(Animations.Snapshot, target, property, to);

    /// <summary>Starts the animations that were given a change.</summary>
    internal void Run()
    {
        foreach (var a in Animations.Snapshot)
        {
            if (HasChanges(a))
                a.Start();
        }
    }

    private static void Reset(IEnumerable<AnimationBase> animations)
    {
        foreach (var a in animations)
        {
            a.Stop();
            if (a is PropertyAnimation pa)
                pa.TransitionTracks.Clear();
            else if (a is AnimationGroup g)
                Reset(g.Animations.Snapshot);
        }
    }

    private static bool Animate(IEnumerable<AnimationBase> animations, QObject target, string property, object? to)
    {
        var animated = false;
        foreach (var a in animations)
        {
            if (a is PropertyAnimation pa && pa.Animates(target, property))
            {
                pa.TransitionTracks.Add((target, property, to));
                animated = true;
            }
            else if (a is AnimationGroup g && Animate(g.Animations.Snapshot, target, property, to))
            {
                animated = true;
            }
        }
        return animated;
    }

    private static bool HasChanges(AnimationBase a) => a switch
    {
        PropertyAnimation pa => pa.TransitionTracks.Count > 0,
        AnimationGroup g => g.Animations.Snapshot.Any(HasChanges),
        _ => false,
    };
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

    public static void Apply(Item item, string previous, string name)
    {
        if (!item.IsComplete)
            return;
        var saved = Saved.GetOrCreateValue(item);
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
        var transition = item.TransitionsList.Snapshot.OfType<Transition>()
            .FirstOrDefault(t => t.Enabled && (Matches(t, previous, name) || (t.Reversible && Matches(t, name, previous))));
        transition?.Reset();
        foreach (var change in state.Changes.Snapshot.OfType<PropertyChanges>())
        {
            foreach (var (target, prop, value) in change.Values(item))
            {
                if (!target.HasProperty(prop))
                    continue;
                using (DependencyTracker.Suspend())
                    saved[(target, prop)] = target.ReadProperty(prop);
                if (transition?.Animate(target, prop, value) != true)
                    Write(target, prop, value);
            }
        }
        transition?.Run();
    }

    private static bool Matches(Transition t, string from, string to) =>
        (t.From == "*" || t.From == from) && (t.To == "*" || t.To == to);

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
