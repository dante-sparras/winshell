namespace Winshell.Runtime.Quickshell;

/// <summary>
/// Quickshell's <c>Reloadable</c>: after a reload, each new object gets the matching object of the old shell,
/// so windows can be reused and state carried over. Objects match by <c>reloadableId</c>, or by their place in the
/// tree.
/// </summary>
public abstract class Reloadable : QObject
{
    private string _reloadableId = "";
    private bool _reloadComplete;

    [Qml]
    public string ReloadableId
    {
        get { Track(); return _reloadableId; }
        set => SetField(ref _reloadableId, value ?? "");
    }

    public bool ReloadDone => _reloadComplete;

    public void Reload(QObject? oldInstance)
    {
        if (_reloadComplete)
            return;
        _reloadComplete = true;
        OnReload(oldInstance);
    }

    /// <summary>Takes what it can from the old instance, then passes the reload on to its children.</summary>
    protected virtual void OnReload(QObject? oldInstance) => ReloadChildrenRecursive(this, oldInstance);

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        if (Engine is null)
            return;
        if (Engine.ReloadFinished)
        {
            // Created after the reload, as a Variants instance or a Loader's item is: nothing old to match.
            Engine.Loop.Post(() =>
            {
                if (!IsDestroyed)
                    Reload(null);
            });
        }
        else
        {
            Engine.ReloadFinishedEvent += OnGenerationReloaded;
        }
    }

    private void OnGenerationReloaded()
    {
        if (Engine is not null)
            Engine.ReloadFinishedEvent -= OnGenerationReloaded;
        if (!IsDestroyed)
            Reload(null);
    }

    protected override void OnDestroy()
    {
        if (Engine is not null)
            Engine.ReloadFinishedEvent -= OnGenerationReloaded;
        base.OnDestroy();
    }

    public static void ReloadRecursive(QObject newObject, QObject? oldRoot)
    {
        if (newObject is Reloadable reloadable)
        {
            QObject? old = null;
            if (oldRoot is not null && reloadable._reloadableId.Length > 0)
                old = ChildByReloadId(oldRoot, reloadable._reloadableId);
            reloadable.Reload(old);
        }
        else
        {
            ReloadChildrenRecursive(newObject, oldRoot);
        }
    }

    public static void ReloadChildrenRecursive(QObject newRoot, QObject? oldRoot)
    {
        foreach (var child in newRoot.OwnedObjects.ToArray())
            ReloadRecursive(child, oldRoot);
    }

    private static QObject? ChildByReloadId(QObject parent, string id)
    {
        foreach (var child in parent.OwnedObjects)
        {
            if (child is Reloadable r)
            {
                if (r._reloadableId == id)
                    return r;
                // Another reloadable is its own reload scope.
                continue;
            }
            if (ChildByReloadId(child, id) is { } found)
                return found;
        }
        return null;
    }
}

/// <summary>Quickshell's <c>Scope</c>: holds objects, and reloads its children by their position.</summary>
[QmlDefaultProperty("children")]
public class Scope : Reloadable
{
    public Scope()
    {
        Children = new QmlList<QObject>(this, "children", added: c => c.SetOwner(this));
    }

    [Qml]
    public QmlList<QObject> Children { get; }

    protected override void OnReload(QObject? oldInstance)
    {
        var old = oldInstance as Scope;
        var children = Children.Snapshot;
        for (var i = 0; i < children.Count; i++)
        {
            var oldChild = old is not null && i < old.Children.Snapshot.Count ? old.Children.Snapshot[i] : null;
            if (children[i] is Reloadable reloadable)
                reloadable.Reload(oldChild is Reloadable ? oldChild : null);
            else
                ReloadRecursive(children[i], oldInstance);
        }
    }
}

/// <summary>Quickshell's <c>ShellRoot</c>: the root of a shell.</summary>
public sealed class ShellRoot : Scope
{
    [Qml]
    public QuickshellSettings Settings { get; } = new();
}

/// <summary>The <c>settings</c> of a ShellRoot.</summary>
public sealed class QuickshellSettings : QObject
{
    private bool _watchFiles = true;

    [Qml]
    public bool WatchFiles
    {
        get { Track(); return _watchFiles; }
        set => SetField(ref _watchFiles, value);
    }

    [Qml]
    public string WorkingDirectory
    {
        get => Environment.CurrentDirectory;
        set => Environment.CurrentDirectory = value;
    }
}

/// <summary>Quickshell's <c>Singleton</c>: the root a <c>pragma Singleton</c> file should use.</summary>
public sealed class Singleton : Scope;

/// <summary>Quickshell's <c>PersistentProperties</c>: properties that keep their values across reloads.</summary>
[QmlSignal("loaded")]
[QmlSignal("reloaded")]
public sealed class PersistentProperties : Reloadable
{
    protected override void OnReload(QObject? oldInstance)
    {
        if (oldInstance is PersistentProperties old)
        {
            foreach (var p in DynamicProperties)
            {
                if (old.GetDynamicProperty(p.Name) is { } oldProperty && !p.IsReadonly)
                    p.Write(oldProperty.Read());
            }
            Emit("reloaded");
        }
        Emit("loaded");
    }
}
