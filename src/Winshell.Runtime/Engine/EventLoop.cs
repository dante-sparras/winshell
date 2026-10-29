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
    private readonly PriorityQueue<TimerEntry, (TimeSpan, long)> _timers = new();
    private readonly List<Action> _frameCallbacks = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _sequence;

    public event Action? Woken;

    public virtual TimeSpan Now => _clock.Elapsed;

    public Log Log { get; set; } = Log.Default;

    /// <summary>Raised with what posted work, a timer or a frame threw, after it is logged. The loop goes on.</summary>
    public event Action<Exception>? Failed;

    public void Post(Action action)
    {
        _posted.Enqueue(action);
        Woken?.Invoke();
    }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var entry = new TimerEntry(action);
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
        var ran = false;
        var posted = new List<Action>();
        while (_posted.TryDequeue(out var action))
            posted.Add(action);
        foreach (var action in posted)
        {
            Run(action);
            ran = true;
        }

        var now = Now;
        var dueNow = new List<TimerEntry>();
        lock (_timers)
        {
            while (_timers.TryPeek(out var entry, out var due) && due.Item1 <= now)
            {
                _timers.Dequeue();
                if (!entry.Cancelled)
                    dueNow.Add(entry);
            }
        }
        foreach (var next in dueNow)
        {
            next.Cancelled = true;
            Run(next.Action);
            ran = true;
        }

        if (HasQueuedWork(now))
            Woken?.Invoke();
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
            }
        }
        return false;
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
        catch (Exception e)
        {
            Log.Error("loop", e.ToString());
            Failed?.Invoke(e);
        }
    }

    private sealed class TimerEntry(Action action) : IDisposable
    {
        public Action Action { get; } = action;

        public bool Cancelled { get; set; }

        public void Dispose() => Cancelled = true;
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
