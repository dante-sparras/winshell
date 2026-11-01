using Microsoft.UI.Dispatching;

namespace Winshell;

/// <summary>The WinUI thread as an event loop. What posted work or a timer throws is logged, and the loop goes on.</summary>
internal sealed class DispatcherLoop(DispatcherQueue queue, Log log) : IEventLoop
{
    private readonly long _started = TimeProvider.System.GetTimestamp();

    public TimeSpan Now => TimeProvider.System.GetElapsedTime(_started);

    public void Post(Action action) => queue.TryEnqueue(() => Run(action));

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            Run(action);
        };
        timer.Start();
        return new Timer(timer);
    }

    public void RequestFrame(Action action) => queue.TryEnqueue(DispatcherQueuePriority.Low, () => Run(action));

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
        }
    }

    private sealed class Timer(DispatcherQueueTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
