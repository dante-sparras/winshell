using System.Collections.Concurrent;
using System.Diagnostics;

namespace Winshell.Runtime;

/// <summary>The thread every QML object lives on. Work from other threads is posted to it.</summary>
public interface IEventLoop
{
    /// <summary>Runs the action on the loop's thread. Safe to call from any thread.</summary>
    void Post(Action action);

    /// <summary>Runs the action once after the delay, on the loop's thread. Dispose to cancel.</summary>
    IDisposable Schedule(TimeSpan delay, Action action);

    /// <summary>Time since the loop started, for timers and animations.</summary>
    TimeSpan Now { get; }

    /// <summary>Runs the action before the next frame is drawn, once per request.</summary>
    void RequestFrame(Action action);
}

/// <summary>
/// The timer queue and posted work shared by the real loop and the test loop. The owner calls
/// <see cref="RunDue"/> when work may be ready, and waits until <see cref="NextDue"/> otherwise.
/// </summary>
public class EventQueue : IEventLoop
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly Queue<Action> _batch = new();
    private readonly PriorityQueue<TimerEntry, (TimeSpan, long)> _timers = new();
    private readonly List<Action> _frameCallbacks = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _sequence;
    private int _cancelled;

    public event Action? Woken;

    public virtual TimeSpan Now => _clock.Elapsed;

    public Log Log { get; set; } = Log.Default;

    /// <summary>
    /// Raised with what posted work, a timer or a frame threw, after it is logged. The loop goes on.
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
            _timers.Enqueue(entry, (Now + delay, _sequence++));
        Woken?.Invoke();
        return entry;
    }

    public void RequestFrame(Action action)
    {
        lock (_frameCallbacks)
        {
            if (!_frameCallbacks.Contains(action))
                _frameCallbacks.Add(action);
        }
        Woken?.Invoke();
    }

    public bool HasFrameRequests
    {
        get
        {
            lock (_frameCallbacks)
                return _frameCallbacks.Count > 0;
        }
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
    /// the message pump. Returns whether anything ran.
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
    /// waits for the next pass, so a handler that posts itself cannot starve the caller. A handler that calls
    /// back in, as <c>waitForJob</c> does, runs the rest of its own batch in order: a result it waits for may be there.
    /// </summary>
    internal bool RunPosted()
    {
        if (_batch.Count == 0)
        {
            while (_posted.TryDequeue(out var queued))
                _batch.Enqueue(queued);
        }
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

    /// <summary>Runs the frame callbacks requested so far.</summary>
    public void RunFrame()
    {
        Action[] callbacks;
        lock (_frameCallbacks)
        {
            callbacks = [.. _frameCallbacks];
            _frameCallbacks.Clear();
        }
        foreach (var callback in callbacks)
            Run(callback);
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (!IsAssertion(e))
        {
            Log.Error("loop", e.ToString());
            Failed?.Invoke(e);
        }
    }

    /// <summary>xUnit failures must leave the loop. Every other exception is a callback the shell logs and continues past.</summary>
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

        public void DropAction() => Action = Dropped;

        public void Dispose() => queue.Cancel(this);
    }
}

/// <summary>An event loop for tests: time moves only when the test advances it.</summary>
public sealed class ManualEventLoop : EventQueue
{
    private TimeSpan _now;

    public override TimeSpan Now => _now;

    /// <summary>Runs everything that is due now, including frames.</summary>
    public void Process()
    {
        for (var i = 0; i < 1000; i++)
        {
            var ran = RunDue();
            if (HasFrameRequests)
            {
                RunFrame();
                ran = true;
            }
            if (!ran)
                return;
        }
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
