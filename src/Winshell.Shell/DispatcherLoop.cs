using Microsoft.UI.Dispatching;

namespace Winshell;

/// <summary>The WinUI thread as an event loop. What posted work or a timer throws is logged, and the loop goes on.</summary>
internal sealed class DispatcherLoop(DispatcherQueue queue, Log log) : IEventLoop
{
    private readonly long _started = TimeProvider.System.GetTimestamp();

    // A timer nothing holds is collected and never fires, and a caller need not keep the handle.
    private readonly HashSet<DispatcherQueueTimer> _timers = [];

    public TimeSpan Now => TimeProvider.System.GetElapsedTime(_started);

    /// <summary>Raised with what posted work or a timer threw, after it is logged.</summary>
    public event Action<Exception>? Failed;

    public void Post(Action action)
    {
        queue.TryEnqueue(() => Run(action));
    }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        lock (_timers)
        {
            _timers.Add(timer);
        }

        timer.Tick += (_, _) =>
        {
            Forget(timer);
            Run(action);
        };
        timer.Start();
        return new Timer(this, timer);
    }

    private void Forget(DispatcherQueueTimer timer)
    {
        timer.Stop();
        lock (_timers)
        {
            _timers.Remove(timer);
        }
    }

    public void RequestFrame(Action action)
    {
        queue.TryEnqueue(DispatcherQueuePriority.Low, () => Run(action));
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Work that fails is reported and the shell keeps going.
        catch (Exception e)
#pragma warning restore CA1031
        {
            log.Error("shell", e.ToString());
            Failed?.Invoke(e);
        }
    }

    private sealed class Timer(DispatcherLoop loop, DispatcherQueueTimer timer) : IDisposable
    {
        public void Dispose()
        {
            loop.Forget(timer);
        }
    }
}
