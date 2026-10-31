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
            if (SetField(ref _when, value) && Owner is Item item)
                States.Reevaluate(item);
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

    /// <summary>
    /// The property assignments, on <see cref="Target"/> (or <paramref name="item"/>) and on targets named by id.
    /// The holder is the object whose binding or value is the expression to apply.
    /// </summary>
    internal IEnumerable<(QObject Target, string Name, QObject Holder)> Assignments(QObject item)
    {
        var target = Target ?? item;
        foreach (var p in DynamicProperties)
        {
            if (p.Name != "target")
                yield return (target, p.Name, this);
        }
        foreach (var (qualifiedTarget, holder) in _qualified ?? [])
        {
            foreach (var p in holder.DynamicProperties)
                yield return (qualifiedTarget, p.Name, holder);
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

    /// <summary>Starts the animations that were given a change. <paramref name="onFinished"/> runs as each of those animations finishes.</summary>
    internal void Run(List<IDisposable> hooks, Action<AnimationBase> onFinished)
    {
        foreach (var a in Animations.Snapshot)
        {
            if (!HasChanges(a))
                continue;
            hooks.Add(a.Connect("finished", _ => onFinished(a)));
            a.Start();
        }
    }

    /// <summary>Whether this animation, or a member of it, was given <paramref name="property"/> on <paramref name="target"/>.</summary>
    internal static bool Covers(AnimationBase animation, QObject target, string property)
    {
        if (animation is PropertyAnimation pa)
            return pa.TransitionTracks.Any(t => ReferenceEquals(t.Target, target) && t.Property == property);
        if (animation is AnimationGroup group)
        {
            foreach (var child in group.Animations.Snapshot)
            {
                if (Covers(child, target, property))
                    return true;
            }
        }
        return false;
    }

    private static void Reset(IEnumerable<AnimationBase> animations)
    {
        foreach (var a in animations)
        {
            a.Stop();
            if (a is PropertyAnimation pa)
                pa.TransitionTracks.Clear();
            else if (a is PropertyAction action)
                action.TransitionTracks.Clear();
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
            else if (a is PropertyAction action && action.Animates(target, property))
            {
                // The action writes the state's value when the sequence reaches it, so the change is not applied now.
                action.TransitionTracks.Add((target, property, to));
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
        PropertyAction action => action.TransitionTracks.Count > 0 || action.Value is not null,
        ScriptAction => true,
        AnimationGroup g => g.Animations.Snapshot.Any(HasChanges),
        _ => false,
    };
}

public static class States
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item, Session> Sessions = new();

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
        var session = Sessions.GetOrCreateValue(item);
        session.Generation++;
        var generation = session.Generation;
        foreach (var hook in session.Hooks)
            hook.Dispose();
        session.Hooks.Clear();
        foreach (var t in item.TransitionsList.Snapshot.OfType<Transition>())
            t.Reset();

        var state = name.Length == 0 ? null : item.StatesList.Snapshot.OfType<State>().FirstOrDefault(s => s.Name == name);
        var next = new Dictionary<(QObject Target, string Property), Change>();
        if (state is not null)
        {
            foreach (var change in state.Changes.Snapshot.OfType<PropertyChanges>())
            {
                foreach (var (target, prop, holder) in change.Assignments(item))
                {
                    if (!target.IsDestroyed && target.HasProperty(prop))
                        next[(target, prop)] = new Change(holder, prop, change.RestoreEntryValues);
                }
            }
        }

        var transition = item.TransitionsList.Snapshot.OfType<Transition>()
            .FirstOrDefault(t => t.Enabled && (Matches(t, previous, name) || (t.Reversible && Matches(t, name, previous))));
        var deferred = new List<Deferred>();

        foreach (var key in session.Saved.Keys.ToArray())
        {
            if (next.ContainsKey(key))
                continue;
            var entry = session.Saved[key];
            if (!entry.Restore || entry.Target.IsDestroyed)
            {
                entry.Slot.Binding?.Dispose();
                session.Saved.Remove(key);
                continue;
            }
            void Commit()
            {
                if (session.Generation != generation || entry.Target.IsDestroyed)
                    return;
                PropertyOverride.Restore(entry.Target, entry.Property, entry.Slot);
                session.Saved.Remove(key);
            }
            if (transition?.Animate(entry.Target, entry.Property, Endpoint(entry.Slot)) == true)
            {
                entry.Target.RemoveBinding(entry.Property);
                deferred.Add(new Deferred(entry.Target, entry.Property, Commit));
            }
            else
            {
                Commit();
            }
        }

        foreach (var (key, change) in next)
        {
            var (target, prop) = key;
            if (session.Saved.TryGetValue(key, out var entry))
            {
                entry.Restore = change.Restore;
                if (!change.Restore)
                {
                    entry.Slot.Binding?.Dispose();
                    entry.Slot = new PropertyOverride.Slot();
                }
            }
            else if (change.Restore)
            {
                session.Saved[key] = new Entry(target, prop, PropertyOverride.Capture(target, prop), true);
            }
            else
            {
                target.RemoveBinding(prop);
            }

            void CommitChange()
            {
                if (session.Generation != generation || target.IsDestroyed)
                    return;
                ApplyChange(target, prop, change.Holder);
            }
            object? to;
            using (DependencyTracker.Suspend())
                to = change.Holder.ReadProperty(change.Name);
            if (transition?.Animate(target, prop, to) == true)
            {
                target.RemoveBinding(prop);
                deferred.Add(new Deferred(target, prop, CommitChange));
            }
            else
            {
                CommitChange();
            }
        }

        if (transition is null)
            return;
        var pending = deferred.ToList();
        transition.Run(session.Hooks, anim =>
        {
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (session.Generation != generation)
                    return;
                var job = pending[i];
                if (!Transition.Covers(anim, job.Target, job.Property))
                    continue;
                pending.RemoveAt(i);
                job.Commit();
            }
        });
    }

    private static void ApplyChange(QObject target, string property, QObject holder)
    {
        if (holder.GetBinding(property) is not null)
        {
            PropertyOverride.InstallLive(target, property, () =>
            {
                if (holder.IsDestroyed || !holder.HasProperty(property))
                    return null;
                return holder.ReadProperty(property);
            }, $"state {target.TypeName}.{property}");
            return;
        }
        target.RemoveBinding(property);
        object? value;
        using (DependencyTracker.Suspend())
            value = holder.ReadProperty(property);
        PropertyOverride.Write(target, property, value);
    }

    private static object? Endpoint(PropertyOverride.Slot slot)
    {
        if (!slot.HadBinding || slot.Binding is null)
            return slot.Value;
        try
        {
            using (DependencyTracker.Suspend())
                return slot.Binding.Peek();
        }
        catch (Exception e)
        {
            Log.Default.Warn("scene", $"State: {e.Message}");
            return slot.Value;
        }
    }

    private static bool Matches(Transition t, string from, string to) =>
        (t.From == "*" || t.From == from) && (t.To == "*" || t.To == to);

    private sealed class Session
    {
        public Dictionary<(QObject Target, string Property), Entry> Saved { get; } = new();

        public int Generation { get; set; }

        public List<IDisposable> Hooks { get; } = [];
    }

    private sealed class Entry(QObject target, string property, PropertyOverride.Slot slot, bool restore)
    {
        public QObject Target { get; } = target;

        public string Property { get; } = property;

        public PropertyOverride.Slot Slot { get; set; } = slot;

        public bool Restore { get; set; } = restore;
    }

    private readonly record struct Change(QObject Holder, string Name, bool Restore);

    private readonly record struct Deferred(QObject Target, string Property, Action Commit);
}
