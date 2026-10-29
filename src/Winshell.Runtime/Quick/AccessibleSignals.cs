namespace Winshell.Runtime.Quick;

/// <summary>
/// Accessible edits, and the items that are notifications, since the last assistive-technology pass.
/// The platform reads this after a frame instead of walking the whole scene to find a notification.
/// </summary>
internal sealed class AccessibleSignals
{
    private readonly HashSet<Item> _notifications = [];

    public bool Dirty { get; private set; }

    public IReadOnlyCollection<Item> Notifications => _notifications;

    public void MarkDirty() => Dirty = true;

    /// <summary>Returns whether an accessible property changed, and clears the flag.</summary>
    public bool TakeDirty()
    {
        if (!Dirty)
            return false;
        Dirty = false;
        return true;
    }

    public void Add(Item item) => _notifications.Add(item);

    public void Remove(Item item) => _notifications.Remove(item);

    public void Prune() => _notifications.RemoveWhere(item => item.IsDestroyed);
}
