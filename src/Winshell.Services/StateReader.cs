namespace Winshell;

/// <summary>
/// Reads a Windows state for one service and hands each reading to the service's loop. One read runs at a time. A
/// request during a read reads once more when it ends, however many requests came, so the last reading is never older
/// than the last request. A read starts on the requesting thread; a backend whose read blocks moves it off that thread.
/// </summary>
internal sealed class StateReader<T>(
    Func<CancellationToken, Task<T>> read,
    Action<T> apply,
    IEventLoop loop,
    MonitorWarning warning)
{
    private readonly object _gate = new();

    // Ends with the reader, so a read that Windows does not cancel ends at its next check.
    private readonly CancellationTokenSource _stopping = new();

    private TaskCompletionSource? _next;
    private bool _reading;
    private int _stopRequested;
    private bool _stopped;
    private Action? _idle;

    private long _revision;

    /// <summary>Reads the state soon.</summary>
    public void Request()
    {
        _ = ReadAsync();
    }

    /// <summary>Reads the state, and ends once a read that started after this call has handed its reading to the loop.</summary>
    public Task ReadAsync()
    {
        TaskCompletionSource next;
        bool start;
        lock (_gate)
        {
            // From the moment Stop begins, so a Windows callback never starts a read that closes the backend on its
            // own thread.
            if (Volatile.Read(ref _stopRequested) != 0)
                return Task.CompletedTask;
            next = _next ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            start = !_reading;
            _reading = true;
        }

        if (start)
            _ = RunAsync();
        return next.Task;
    }

    /// <summary>Drops the reading of a read already running, because something since has made it stale.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _revision);
    }

    /// <summary>
    /// Stops reading and drops a reading not yet applied. <paramref name="idle"/> runs once no read is running: now, or
    /// when the running read ends.
    /// </summary>
    public void Stop(Action? idle = null)
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) == 1)
            return;
        _stopping.Cancel();
        TaskCompletionSource? waiting;
        bool now;
        lock (_gate)
        {
            _stopped = true;
            Invalidate();
            waiting = _next;
            _next = null;
            now = !_reading;
            if (!now)
                _idle = idle;
        }

        waiting?.TrySetResult();
        if (now)
            Close(idle);
    }

    private async Task RunAsync()
    {
        TaskCompletionSource? finished = null;
        while (true)
        {
            TaskCompletionSource? next;
            Action? idle = null;
            bool stopped;
            lock (_gate)
            {
                stopped = _stopped;
                next = stopped ? null : _next;
                _next = null;
                if (next is null)
                {
                    _reading = false;
                    idle = _idle;
                    _idle = null;
                }
            }

            if (next is null)
            {
                try
                {
                    if (stopped)
                        Close(idle);
                }
                finally
                {
                    finished?.TrySetResult();
                }

                return;
            }

            finished?.TrySetResult();
            finished = next;
            await ReadOnceAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadOnceAsync()
    {
        var revision = Interlocked.Read(ref _revision);
        T reading;
        try
        {
            reading = await read(_stopping.Token).ConfigureAwait(false);
            warning.Succeeded();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
#pragma warning disable CA1031 // Nobody awaits a requested read, and one failed read must not stop the next.
        catch (Exception e)
#pragma warning restore CA1031
        {
            warning.Failed(e);
            return;
        }

        loop.Post(() =>
        {
            if (revision == Interlocked.Read(ref _revision))
                apply(reading);
        });
    }

    private void Close(Action? idle)
    {
        try
        {
            idle?.Invoke();
        }
#pragma warning disable CA1031 // Closing runs as its service is disposed; a failure there must not stop the rest.
        catch (Exception e)
#pragma warning restore CA1031
        {
            warning.Failed(e);
        }
        finally
        {
            _stopping.Dispose();
        }
    }
}
