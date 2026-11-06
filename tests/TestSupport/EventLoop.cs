using System.Collections.Concurrent;

namespace Winshell.TestSupport;

/// <summary>
/// Posted work and timers for tests. A test calls <see cref="RunDue"/> when work may be ready, and reads
/// <see cref="NextDue"/> to see when the next timer is due. <see cref="ManualEventLoop"/> moves time itself.
/// </summary>
internal class EventQueue : IEventLoop
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly Queue<Action> _batch = new();
    private readonly PriorityQueue<TimerEntry, (TimeSpan, long)> _timers = new();
    private readonly long _started = TimeProvider.System.GetTimestamp();
    private long _sequence;
    private int _cancelled;

    public event Action? Woken;

    public virtual TimeSpan Now => TimeProvider.System.GetElapsedTime(_started);

    public Log Log { get; set; } = Log.Default;

    /// <summary>
    /// Raised with what posted work or a timer threw, after it is logged. The loop goes on.
    /// An xUnit assertion is not caught: it fails the caller, so a test does not hang until the job times out.
    /// </summary>
    public event Action<Exception>? Failed;

    public void Post(Action action)
    {
        _posted.Enqueue(action);
        Woken?.Invoke();
    }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var entry = new TimerEntry(this, action);
        lock (_timers)
        {
            _timers.Enqueue(entry, (Now + delay, _sequence++));
        }

        Woken?.Invoke();
        return entry;
    }

    /// <summary>When the next timer is due, or null when there is none.</summary>
    public TimeSpan? NextDue
    {
        get
        {
            lock (_timers)
            {
                while (_timers.TryPeek(out var entry, out var due))
                {
                    if (!entry.Cancelled)
                        return due.Item1;
                    _timers.Dequeue();
                    _cancelled--;
                }

                return null;
            }
        }
    }

    /// <summary>
    /// Runs one batch of posted work and timers that were already due. Work queued by that batch waits for the
    /// next pass, so a handler that posts itself or a timer that fires slower than its interval cannot starve
    /// the caller. Returns whether anything ran.
    /// </summary>
    public bool RunDue()
    {
        var ran = RunPosted();

        var now = Now;
        var dueNow = new List<TimerEntry>();
        lock (_timers)
        {
            while (_timers.TryPeek(out var entry, out var due) && due.Item1 <= now)
            {
                _timers.Dequeue();
                if (entry.Cancelled)
                {
                    _cancelled--;
                    continue;
                }

                entry.Cancelled = true;
                dueNow.Add(entry);
            }
        }

        foreach (var next in dueNow)
        {
            Run(next.Action);
            ran = true;
        }

        if (HasQueuedWork(now))
            Woken?.Invoke();
        return ran;
    }

    /// <summary>
    /// Runs the posted work queued so far and leaves timers for <see cref="RunDue"/>. Work posted by this batch
    /// waits for the next pass. A handler that calls back in runs the rest of its own batch in order.
    /// </summary>
    internal bool RunPosted()
    {
        if (_batch.Count == 0)
            while (_posted.TryDequeue(out var queued))
                _batch.Enqueue(queued);
        var ran = _batch.Count > 0;
        while (_batch.TryDequeue(out var action))
            Run(action);
        return ran;
    }

    /// <summary>Whether a later pass still has posted work, or a timer that was due at <paramref name="now"/>.</summary>
    private bool HasQueuedWork(TimeSpan now)
    {
        if (!_posted.IsEmpty)
            return true;
        lock (_timers)
        {
            while (_timers.TryPeek(out var entry, out var due))
            {
                if (!entry.Cancelled)
                    return due.Item1 <= now;
                _timers.Dequeue();
                _cancelled--;
            }
        }

        return false;
    }

    /// <summary>
    /// Cancels a timer that has not fired. It leaves the heap when it reaches the top, or sooner when cancelled
    /// timers pass half the heap, so a timer restarted on every keystroke does not pile up until its due time.
    /// </summary>
    private void Cancel(TimerEntry entry)
    {
        lock (_timers)
        {
            if (entry.Cancelled)
                return;
            entry.Cancelled = true;
            // The entry stays in the heap until it is due when other timers are still live. The action closes over
            // the object that scheduled it, so leaving it there keeps that object until the due time.
            entry.DropAction();
            if (++_cancelled <= _timers.Count / 2)
                return;
            var live = _timers.UnorderedItems.Where(item => !item.Element.Cancelled).ToList();
            _timers.Clear();
            _timers.EnqueueRange(live);
            _cancelled = 0;
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // A callback that fails is reported and the loop goes on. An assertion fails the test.
        catch (Exception e) when (!IsAssertion(e))
#pragma warning restore CA1031
        {
            Log.Error("loop", e.ToString());
            Failed?.Invoke(e);
        }
    }

    /// <summary>xUnit failures must leave the loop. Every other exception is logged, and the loop goes on.</summary>
    private static bool IsAssertion(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var name = current.GetType().FullName;
            if (name is not null && name.StartsWith("Xunit.Sdk.", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private sealed class TimerEntry(EventQueue queue, Action action) : IDisposable
    {
        private static readonly Action Dropped = static () => { };

        public Action Action { get; private set; } = action;

        /// <summary>Set under the timer lock when the timer is cancelled or taken off the heap to fire.</summary>
        public bool Cancelled { get; set; }

        public void DropAction()
        {
            Action = Dropped;
        }

        public void Dispose()
        {
            queue.Cancel(this);
        }
    }
}

/// <summary>An event loop for tests: time moves only when the test advances it.</summary>
internal sealed class ManualEventLoop : EventQueue
{
    private TimeSpan _now;

    public override TimeSpan Now => _now;

    /// <summary>Runs everything that is due now.</summary>
    public void Process()
    {
        for (var i = 0; i < 1000; i++)
            if (!RunDue())
                return;
    }

    /// <summary>Moves time forward, running each timer when its time comes.</summary>
    public void Advance(TimeSpan by)
    {
        var end = _now + by;
        Process();
        while (NextDue is { } due && due <= end)
        {
            _now = due;
            Process();
        }

        _now = end;
        Process();
    }
}
