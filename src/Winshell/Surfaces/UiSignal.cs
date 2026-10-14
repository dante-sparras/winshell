using Microsoft.UI.Dispatching;

namespace Winshell;

/// <summary>
/// Turns a burst of signals from any thread into one run on the UI thread, <c>delay</c> after the first.
/// While signals keep coming the timer keeps turning, so the UI thread wakes once per delay, and each turn runs when a signal came since the last.
/// The first turn that finds no signal ends the burst, and the next signal arms the timer again.
/// </summary>
internal sealed class UiSignal
{
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueHandler _arm;
    private int _armed;
    private int _signaled;

    public UiSignal(DispatcherQueue queue, TimeSpan delay, Action run)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(run);
        _queue = queue;
        _timer = queue.CreateTimer();
        _timer.Interval = delay;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref _signaled, 0) != 0)
            {
                try
                {
                    run();
                }
                finally
                {
                    // Armed still, so a signal during the burst costs the UI thread no wake of its own.
                    _timer.Start();
                }

                return;
            }

            // Each side writes its flag with a full fence and then reads the other's, so one of the two always sees the other.
            Interlocked.Exchange(ref _armed, 0);

            // A signal that came between the two tests above saw the timer armed and did not arm it.
            if (Volatile.Read(ref _signaled) != 0)
            {
                Arm();
            }
        };
        _arm = () => _timer.Start();
    }

    /// <summary>Asks for a run. Cheap and safe from any thread, including inside a hook.</summary>
    public void Signal()
    {
        if (Volatile.Read(ref _signaled) == 0)
        {
            Interlocked.Exchange(ref _signaled, 1);
        }

        if (Volatile.Read(ref _armed) == 0)
        {
            Arm();
        }
    }

    /// <summary>Drops a run that has not started. Used at shutdown.</summary>
    public void Stop() => _timer.Stop();

    private void Arm()
    {
        if (Interlocked.Exchange(ref _armed, 1) == 0 && !_queue.TryEnqueue(_arm))
        {
            Volatile.Write(ref _armed, 0);
        }
    }
}

