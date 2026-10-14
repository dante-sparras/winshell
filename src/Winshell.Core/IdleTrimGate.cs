namespace Winshell.Core;

/// <summary>
/// Decides when the desktop gives memory back. The full trim waits until the person has been away a short while,
/// and runs again only once enough has been allocated since to be worth another pause, less often the longer nobody returns.
/// The sweep is the small collection between trims, and does not wait for the person to be away.
/// </summary>
public sealed class IdleTrimGate
{
    /// <summary>Seconds without input before a trim may run.</summary>
    public const int IdleSeconds = 20;

    /// <summary>The shortest time between two trims, in milliseconds.</summary>
    public const long GapMs = 120_000;

    /// <summary>The longest wait between two trims while nobody has returned, in milliseconds.</summary>
    public const long LongestGapMs = 30 * 60_000;

    /// <summary>Bytes that must have been allocated since the last trim for the next one to run.</summary>
    public const long WorthBytes = 8L * 1024 * 1024;

    /// <summary>The shortest time between a collection of any kind and a sweep, in milliseconds.</summary>
    public const long SweepGapMs = 30_000;

    /// <summary>Bytes that must have been allocated since the last collection of any kind for a sweep to run.</summary>
    public const long SweepBytes = 4L * 1024 * 1024;

    private bool _trimmed;
    private bool _unattended;
    private long _at;
    private long _allocated;
    private long _gap = GapMs;
    private bool _counting;
    private int _collections;
    private long _collectedAt;
    private long _collectedAllocated;

    /// <summary>True when a trim should run now. The caller runs it.</summary>
    /// <param name="now">Milliseconds from a steady clock.</param>
    /// <param name="idleSeconds">Seconds since the last user input.</param>
    /// <param name="busy">True while something is on screen that a pause would show in, such as an open panel.</param>
    /// <param name="allocatedBytes">Bytes the process has allocated since it started.</param>
    /// <returns>
    /// True at most once per <see cref="GapMs"/>. Each trim that follows another with no input between them
    /// waits twice as long as the one before, up to <see cref="LongestGapMs"/>.
    /// </returns>
    public bool ShouldTrim(long now, int idleSeconds, bool busy, long allocatedBytes)
    {
        if (idleSeconds < IdleSeconds)
        {
            // The person is back. The next time they leave, the trims start at the short gap again.
            _unattended = false;
            _gap = GapMs;
            return false;
        }

        if (busy)
        {
            return false;
        }

        if (_trimmed && (now - _at < _gap || allocatedBytes - _allocated < WorthBytes))
        {
            return false;
        }

        if (_unattended)
        {
            _gap = Math.Min(_gap * 2, LongestGapMs);
        }

        Trimmed(now, allocatedBytes);
        return true;
    }

    /// <summary>Records a trim the caller ran without asking, such as the one at lock, so the next one counts from it.</summary>
    /// <param name="now">Milliseconds from a steady clock.</param>
    /// <param name="allocatedBytes">Bytes the process has allocated since it started.</param>
    public void Trimmed(long now, long allocatedBytes)
    {
        _trimmed = true;
        _unattended = true;
        _at = now;
        _allocated = allocatedBytes;
    }

    /// <summary>True when a sweep, a small collection of the young generations, should run now. The caller runs it.</summary>
    /// <param name="now">Milliseconds from a steady clock.</param>
    /// <param name="busy">True while something is on screen that a pause would show in, such as an open panel.</param>
    /// <param name="allocatedBytes">Bytes the process has allocated since it started.</param>
    /// <param name="collections">How many collections of any kind the process has run since it started.</param>
    /// <returns>
    /// True when <see cref="SweepBytes"/> were allocated and <see cref="SweepGapMs"/> passed since the last collection of any kind,
    /// a trim and the collector's own among them. It does not wait for the person to be away.
    /// </returns>
    public bool ShouldSweep(long now, bool busy, long allocatedBytes, int collections)
    {
        if (!_counting || collections != _collections)
        {
            // Something collected since the last look: the trim, a sweep, or the collector by itself. Count from here.
            _counting = true;
            _collections = collections;
            _collectedAt = now;
            _collectedAllocated = allocatedBytes;
            return false;
        }

        if (busy || now - _collectedAt < SweepGapMs || allocatedBytes - _collectedAllocated < SweepBytes)
        {
            return false;
        }

        _collectedAt = now;
        _collectedAllocated = allocatedBytes;
        return true;
    }
}
