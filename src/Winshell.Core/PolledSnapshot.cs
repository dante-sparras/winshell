using Winshell.Core.Diagnostics;

namespace Winshell.Core;

/// <summary>
/// One value refreshed by a background read.
/// At most one paced read is in flight. The next read waits until the interval has elapsed since the last attempt finished, including when that attempt threw.
/// A read that throws twice or more in a row waits longer each time, up to a minute, until one succeeds.
/// A read still in flight a minute after it started is given up on when the next one is asked for, and counts as one that threw.
/// While a panel that shows the value is open, a shorter interval can apply.
/// The latest read to start wins: a result that lands after a newer read started, or after <see cref="Drop"/> or <see cref="SetOpen"/>, is thrown away.
/// A value equal to the one on hand keeps the object on hand, so a reader can tell an unchanged value by reference, and <see cref="Changed"/> stays quiet.
/// </summary>
/// <typeparam name="T">The value callers read.</typeparam>
public sealed class PolledSnapshot<T>
{
    private readonly long _intervalMilliseconds;
    private readonly long _openIntervalMilliseconds;
    private readonly Func<long> _clock;
    private readonly object _gate = new();
    private T _current;
    private int _busy;
    private long _startedAt;
    private long _completedAt;
    private bool _attempted;
    private bool _open;
    private long _generation;
    private long _reads;
    private int _failures;

    /// <summary>The longest a read that keeps throwing waits, unless its own interval is longer.</summary>
    internal const long FailedCeilingMilliseconds = 60_000;

    /// <summary>How long a paced read may be in flight before the next <see cref="Kick(Func{Task{T}})"/> or <see cref="Refresh"/> gives up on it.</summary>
    internal const long ReadLimitMilliseconds = 60_000;

    /// <summary>
    /// Raised after <see cref="Current"/> took a value that differs from the one before, on the thread that stored it:
    /// the thread pool for a read, the caller for <see cref="Assign(T)"/> and <see cref="Store"/>. A handler must not block.
    /// </summary>
    public event Action? Changed;

    /// <summary>Creates a snapshot that has not yet attempted a read.</summary>
    /// <param name="intervalMilliseconds">How long a finished attempt, success or failure, holds off the next one. Zero allows a kick whenever nothing is in flight.</param>
    /// <param name="clock">Millisecond clock. It is read under the snapshot lock and must not call back into this instance.</param>
    /// <param name="initial">The value of <see cref="Current"/> until a read succeeds.</param>
    public PolledSnapshot(long intervalMilliseconds, Func<long> clock, T initial)
        : this(intervalMilliseconds, intervalMilliseconds, clock, initial)
    {
    }

    /// <summary>Creates a snapshot that reads more often while its panel is open.</summary>
    /// <param name="intervalMilliseconds">How long a finished attempt holds off the next one while the panel is closed.</param>
    /// <param name="openIntervalMilliseconds">How long a finished attempt holds off the next one while the panel is open.</param>
    /// <param name="clock">Millisecond clock. It is read under the snapshot lock and must not call back into this instance.</param>
    /// <param name="initial">The value of <see cref="Current"/> until a read succeeds.</param>
    public PolledSnapshot(long intervalMilliseconds, long openIntervalMilliseconds, Func<long> clock, T initial)
    {
        if (intervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMilliseconds), intervalMilliseconds, "The interval cannot be negative.");
        }

        if (openIntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openIntervalMilliseconds), openIntervalMilliseconds, "The interval cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(clock);
        _intervalMilliseconds = intervalMilliseconds;
        _openIntervalMilliseconds = openIntervalMilliseconds;
        _clock = clock;
        _current = initial;
    }

    /// <summary>The latest successful value, or the initial value when every attempt has failed.</summary>
    public T Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// True only while a paced read from <see cref="Kick(Func{Task{T}})"/> or <see cref="Refresh"/> is in flight.
    /// A read past <see cref="ReadLimitMilliseconds"/> still counts until the next one is asked for.
    /// </summary>
    public bool Busy
    {
        get
        {
            lock (_gate)
            {
                return _busy != 0;
            }
        }
    }

    /// <summary>True while the panel that shows this value is open. See <see cref="SetOpen"/>.</summary>
    public bool Open
    {
        get
        {
            lock (_gate)
            {
                return _open;
            }
        }
    }

    /// <summary>How many read results have been stored. <see cref="Assign(T)"/> does not count.</summary>
    public long Reads
    {
        get
        {
            lock (_gate)
            {
                return _reads;
            }
        }
    }

    /// <summary>Milliseconds until a paced read is due. Zero when one is due now. While one is in flight, the whole interval.</summary>
    public long Wait
    {
        get
        {
            lock (_gate)
            {
                if (_busy != 0)
                {
                    return Interval;
                }

                if (!_attempted)
                {
                    return 0;
                }

                return Math.Max(0, Interval - (_clock() - _completedAt));
            }
        }
    }

    // The first failure retries at the usual pace, since one read can fail by chance. Each one after it doubles the wait.
    private long Interval
    {
        get
        {
            var pace = _open ? _openIntervalMilliseconds : _intervalMilliseconds;
            return _failures < 2
                ? pace
                : Math.Min(pace << Math.Min(_failures - 1, 6), Math.Max(pace, FailedCeilingMilliseconds));
        }
    }

    /// <summary>
    /// Starts <paramref name="read"/> when the interval has elapsed and nothing is in flight.
    /// Returns immediately. A second call while that read is in flight does not start another.
    /// The read is queued on the thread pool; this method does not call it.
    /// Success stores the value and starts the interval. A throw, from the read or from storing its result, leaves <see cref="Current"/> unchanged, clears <see cref="Busy"/>, and starts the interval.
    /// </summary>
    /// <param name="read">Supplies the next value. It runs off the caller's thread.</param>
    public void Kick(Func<Task<T>> read) => Kick(read, Replace);

    /// <summary>
    /// Starts <paramref name="read"/> as <see cref="Kick(Func{Task{T}})"/> does, and stores what <paramref name="merge"/> makes of the current value and the result.
    /// </summary>
    /// <param name="read">Supplies the next value. It runs off the caller's thread.</param>
    /// <param name="merge">Maps the current value and the result to the stored value. It runs under the snapshot lock and must not call back into this instance.</param>
    public void Kick(Func<Task<T>> read, Func<T, T, T> merge)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(merge);
        if (!Take(out var generation))
        {
            return;
        }

        _ = Task.Run(() => Run(read, merge, generation));
    }

    /// <summary>
    /// Runs <paramref name="read"/> on the caller when a read is due, as <see cref="Kick(Func{Task{T}}, Func{T, T, T})"/> would, and finishes when it does.
    /// A loop that owns the pace awaits this, then waits <see cref="Wait"/>.
    /// </summary>
    /// <param name="read">Supplies the next value.</param>
    /// <param name="merge">Maps the current value and the result to the stored value. Null stores the result as it is.</param>
    /// <returns>A task that ends when the read and its store have, or at once when no read was due.</returns>
    public Task Refresh(Func<Task<T>> read, Func<T, T, T>? merge = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Take(out var generation) ? Run(read, merge ?? Replace, generation) : Task.CompletedTask;
    }

    /// <summary>
    /// Starts a read the caller runs and stores itself with <see cref="Store"/>, outside the pace.
    /// A paced read already in flight is thrown away when it lands, and counts as an attempt.
    /// </summary>
    /// <returns>The ticket <see cref="Store"/> and <see cref="Latest"/> take.</returns>
    public long Begin()
    {
        lock (_gate)
        {
            if (_busy != 0)
            {
                _busy = 0;
                Stamp();
            }

            return ++_generation;
        }
    }

    /// <summary>True while no read has started since <paramref name="ticket"/>, and nothing was dropped.</summary>
    /// <param name="ticket">What <see cref="Begin"/> returned.</param>
    /// <returns>True when a result for that ticket would still be stored.</returns>
    public bool Latest(long ticket)
    {
        lock (_gate)
        {
            return ticket == _generation;
        }
    }

    /// <summary>Stores a result for <paramref name="ticket"/> unless a newer read started since. The pace is not touched.</summary>
    /// <param name="ticket">What <see cref="Begin"/> returned.</param>
    /// <param name="value">The result.</param>
    /// <param name="merge">Maps the current value and the result to the stored value. Null stores the result as it is.</param>
    /// <returns>True when the result was stored.</returns>
    public bool Store(long ticket, T value, Func<T, T, T>? merge = null)
    {
        bool changed;
        lock (_gate)
        {
            if (ticket != _generation)
            {
                return false;
            }

            changed = Put((merge ?? Replace)(_current, value));
            _reads++;
        }

        Raise(changed);
        return true;
    }

    /// <summary>
    /// Stores <paramref name="value"/> and does not start the interval.
    /// An in-flight <see cref="Kick(Func{Task{T}})"/> can replace <see cref="Current"/> when it finishes.
    /// </summary>
    /// <param name="value">The value callers should see now.</param>
    public void Assign(T value)
    {
        bool changed;
        lock (_gate)
        {
            changed = Put(value);
        }

        Raise(changed);
    }

    /// <summary>
    /// Stores the result of <paramref name="change"/> and does not start the interval.
    /// An in-flight <see cref="Kick(Func{Task{T}})"/> can replace <see cref="Current"/> when it finishes.
    /// </summary>
    /// <param name="change">Maps the current value. It runs under the snapshot lock and must not call back into this instance.</param>
    public void Assign(Func<T, T> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        bool changed;
        lock (_gate)
        {
            changed = Put(change(_current));
        }

        Raise(changed);
    }

    /// <summary>
    /// Starts the interval from the current clock without changing <see cref="Current"/>.
    /// An in-flight read still starts the interval again when it finishes.
    /// </summary>
    public void Arm()
    {
        lock (_gate)
        {
            Stamp();
        }
    }

    /// <summary>
    /// Drops the last attempt so the next <see cref="Kick(Func{Task{T}})"/> can run, at the usual pace again after failures.
    /// <see cref="Current"/> stays. An in-flight read still starts the interval when it finishes.
    /// </summary>
    public void Expire()
    {
        lock (_gate)
        {
            _attempted = false;
            _failures = 0;
        }
    }

    /// <summary>
    /// Throws away any read in flight when it lands, and lets the next read run at once.
    /// Use it when what the value describes has changed, such as a new weather place.
    /// </summary>
    public void Drop()
    {
        lock (_gate)
        {
            DropCore();
        }
    }

    /// <summary>
    /// Switches between the open and closed interval. A change throws away any read in flight, which was the other kind,
    /// and lets the next read run at once.
    /// </summary>
    /// <param name="open">True while the panel that shows this value is open.</param>
    public void SetOpen(bool open)
    {
        lock (_gate)
        {
            if (open == _open)
            {
                return;
            }

            _open = open;
            DropCore();
        }
    }

    private static T Replace(T current, T value) => value;

    private void DropCore()
    {
        _generation++;
        _busy = 0;
        _attempted = false;
        _failures = 0;
    }

    /// <summary>Stores <paramref name="next"/> unless it equals the value on hand, which then stays the object readers hold. Call under the gate.</summary>
    /// <returns>True when the value changed.</returns>
    private bool Put(T next)
    {
        if (EqualityComparer<T>.Default.Equals(_current, next))
        {
            return false;
        }

        _current = next;
        return true;
    }

    // Outside the gate, so a handler may read this snapshot.
    private void Raise(bool changed)
    {
        if (!changed)
        {
            return;
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning("Plugin", "a snapshot change was not heard", ex);
        }
    }

    /// <summary>Takes the next paced read when one is due, after giving up on one that has been in flight past the limit.</summary>
    /// <param name="generation">The generation of the read the caller now runs.</param>
    /// <returns>True when the caller runs a read.</returns>
    private bool Take(out long generation)
    {
        var gaveUp = false;
        var due = false;
        generation = 0;
        lock (_gate)
        {
            if (_busy != 0 && _clock() - _startedAt >= ReadLimitMilliseconds)
            {
                // A call that hangs would hold the snapshot busy for good. Its thread cannot be stopped, so it is left behind:
                // the new generation throws its result away if it ever lands, and it cannot free a read that started after it.
                _generation++;
                _busy = 0;
                if (_failures < int.MaxValue)
                {
                    _failures++;
                }

                Stamp();
                gaveUp = true;
            }

            if (Due())
            {
                _busy = 1;
                _startedAt = _clock();
                generation = ++_generation;
                due = true;
            }
        }

        if (gaveUp)
        {
            Log.Warning("Plugin", "a snapshot read did not finish in a minute and was given up on");
        }

        return due;
    }

    private bool Due()
    {
        if (_busy != 0)
        {
            return false;
        }

        if (!_attempted)
        {
            return true;
        }

        return _clock() - _completedAt >= Interval;
    }

    private async Task Run(Func<Task<T>> read, Func<T, T, T> merge, long generation)
    {
        var success = false;
        var changed = false;
        T value = default!;
        try
        {
            var task = read();
            value = await task.ConfigureAwait(false);
            success = true;
        }
        catch
        {
            success = false;
        }

        Exception? unstored = null;
        lock (_gate)
        {
            if (generation != _generation)
            {
                // A newer read started, or the read was dropped. Its owner runs the pace now.
                return;
            }

            if (success)
            {
                try
                {
                    changed = Put(merge(_current, value));
                    _reads++;
                    _failures = 0;
                }
                catch (Exception ex)
                {
                    // A merge or a comparison that throws is a failed read. Let out of here it would leave the snapshot busy,
                    // and no later read would start.
                    success = false;
                    unstored = ex;
                }
            }

            if (!success && _failures < int.MaxValue)
            {
                _failures++;
            }

            Stamp();
            _busy = 0;
        }

        if (unstored is not null)
        {
            Log.Warning("Plugin", "a snapshot read was not stored", unstored);
        }

        Raise(changed);
    }

    private void Stamp()
    {
        _completedAt = _clock();
        _attempted = true;
    }
}
