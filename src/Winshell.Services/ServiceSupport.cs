using System.Globalization;

namespace Winshell;

/// <summary>Helpers the Windows service modules share.</summary>
internal static class ServiceSupport
{
    /// <summary>
    /// Runs background work and logs what it throws, so a failing Windows API does not take the shell down.
    /// </summary>
    public static void Run(string category, Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A Windows service that fails is reported and the shell keeps going.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Log.Default.Warn(category, e.Message);
            }
        });

    /// <summary>Releases a cancelled monitor's token source only after its task has ended, without waiting.</summary>
    public static void DisposeAfterMonitor(CancellationTokenSource stop, Task? monitor)
    {
        if (monitor is null || monitor.IsCompleted)
            stop.Dispose();
        else
            _ = monitor.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), stop,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>A MAC address as <c>XX:XX:XX:XX:XX:XX</c>.</summary>
    public static string FormatMac(ulong address) =>
        string.Join(':', Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xff).ToString("X2", CultureInfo.InvariantCulture)));

    public static string FormatMac(ReadOnlySpan<byte> bytes) =>
        string.Join(':', bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}

/// <summary>
/// A process-wide source of snapshots that each engine's singleton follows. Windows events arrive on
/// other threads; listeners are told on whatever thread the change happened and post to their loop.
/// </summary>
internal abstract class SnapshotSource<T>
    where T : class
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private T _current;
    private Action<T>? _listeners;
    private bool _started;
    private int _refreshQueued;

    protected SnapshotSource(T initial) => _current = initial;

    protected abstract string Category { get; }

    public T Current
    {
        get
        {
            lock (_lock)
                return _current;
        }
    }

    /// <summary>Starts the source the first time, and calls <paramref name="listener"/> with each new snapshot.</summary>
    public IDisposable Subscribe(Action<T> listener)
    {
        bool start;
        lock (_lock)
        {
            _listeners += listener;
            start = !_started;
            _started = true;
        }
        if (start)
            ServiceSupport.Run(Category, StartAsync);
        return new Subscription(() =>
        {
            lock (_lock)
                _listeners -= listener;
        });
    }

    /// <summary>Subscribes to the Windows events and makes the first snapshot.</summary>
    protected abstract Task StartAsync();

    /// <summary>Reads the current state from Windows.</summary>
    protected abstract Task<T> ReadAsync();

    /// <summary>Reads the state again soon. Calls that come while a read is queued share it.</summary>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
            return;
        ServiceSupport.Run(Category, async () =>
        {
            await Task.Delay(50).ConfigureAwait(false);
            Interlocked.Exchange(ref _refreshQueued, 0);
            await RefreshNowAsync().ConfigureAwait(false);
        });
    }

    protected async Task RefreshNowAsync()
    {
        // One read at a time, so an older read never replaces a newer one.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var next = await ReadAsync().ConfigureAwait(false);
            Action<T>? listeners;
            lock (_lock)
            {
                _current = next;
                listeners = _listeners;
            }
            listeners?.Invoke(next);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
