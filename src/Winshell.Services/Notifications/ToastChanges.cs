namespace Winshell.Notifications;

/// <summary>
/// Whether the toasts may have changed since they were last read. Reading them costs Windows about 100 ms of CPU, so
/// the idle poll reads them again only when the backend's stamp has changed. The stamp is only a hint: when there is
/// none every poll reads, and a change event, a dismissal and a minute without a read also count, so a change it does
/// not show is read within <see cref="MaxAge"/>.
/// </summary>
/// <param name="stamp">The backend's stamp, or null when there is none, which counts as a change.</param>
/// <param name="time">Times the minute between reads.</param>
internal sealed class ToastChanges(Func<string?> stamp, TimeProvider time)
{
    /// <summary>The longest the toasts go without being read while the poll runs.</summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    private string? _readStamp;
    private int _readChanges = -1;
    private long _readAt;
    private int _changes;

    /// <summary>
    /// Takes the stamp before a read. True when the toasts must be read; <see cref="Read"/> follows a read that worked,
    /// and until one does they stay due.
    /// </summary>
    public bool Due(out (string? Stamp, int Changes) current)
    {
        current = (stamp(), Volatile.Read(ref _changes));
        return current.Stamp is null || current.Stamp != _readStamp || current.Changes != _readChanges ||
               time.GetElapsedTime(_readAt) >= MaxAge;
    }

    /// <summary>The toasts were read as they were at <paramref name="current"/>.</summary>
    public void Read((string? Stamp, int Changes) current)
    {
        _readStamp = current.Stamp;
        _readChanges = current.Changes;
        _readAt = time.GetTimestamp();
    }

    /// <summary>The toasts changed: the next read reads them, even one that started before this.</summary>
    public void Change()
    {
        Interlocked.Increment(ref _changes);
    }
}
