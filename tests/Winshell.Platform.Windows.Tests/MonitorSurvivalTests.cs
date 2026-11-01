using Winshell.Platform.Windows.Services.Bluetooth;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A failed read or a reload must not stop the Bluetooth or notification poll.</summary>
public class MonitorSurvivalTests
{
    [Fact]
    public async Task A_throwing_bluetooth_read_does_not_stick_the_monitor()
    {
        var backend = new FlakyBluetooth();
        var loop = new ManualEventLoop();
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };
        using var engine = new QmlEngine(StandardModules.Create(), loop, log);
        var service = new BluetoothService(engine, backend);
        await backend.First.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (backend.Reads < 2 && DateTime.UtcNow < until)
        {
            await service.RefreshAsync();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(backend.Reads >= 2, $"reads stayed at {backend.Reads}");
        loop.Process();
        Assert.True(service.Ready);
        Assert.Contains(messages, m => m.Contains("radio broke", StringComparison.Ordinal));
        Assert.False(service.MonitorTask!.IsFaulted);
        Assert.False(service.MonitorTask.IsCompleted);
        service.Destroy();
        Assert.True(backend.Disposed);
        await service.MonitorTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(service.MonitorTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_refresh_still_reads_after_the_monitor_stops()
    {
        var backend = new TokenBluetooth();
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var service = new BluetoothService(engine, backend);
        await backend.First.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        service.StopMonitoring();
        var before = backend.Finished;

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (backend.Finished == before && DateTime.UtcNow < until)
        {
            await service.RefreshAsync();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(backend.Finished > before, "A refresh was cancelled along with the monitor.");
        service.Destroy();
    }

    [Fact]
    public async Task A_refresh_during_a_read_reads_again_when_it_ends()
    {
        var backend = new GatedBluetooth();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        var first = service.RefreshAsync();
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await service.RefreshAsync();
        Assert.Equal(1, backend.Reads);
        // Resumes the read on this thread, so the refresh finishes without a pool thread.
        backend.Gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (backend.Reads < 2 && DateTime.UtcNow < until)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(2, backend.Reads);
        service.Destroy();
    }

    [Fact]
    public async Task Destroying_bluetooth_waits_for_the_read_before_disposing_the_backend()
    {
        var backend = new BlockingBluetooth();
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var service = new BluetoothService(engine, backend);
        // IsCompleted, not await: the signal completes on the monitor thread, and awaiting it queues a
        // continuation on the thread pool that a busy pool never ran.
        var untilStarted = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!backend.Started.Task.IsCompleted && DateTime.UtcNow < untilStarted)
            Thread.Sleep(10);
        Assert.True(backend.Started.Task.IsCompleted);

        var started = DateTime.UtcNow;
        service.Destroy();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(1.5), $"destroy waited {elapsed}");
        await service.MonitorTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(backend.Disposed);
        Assert.True(service.MonitorTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_throwing_notification_read_leaves_the_listener_polling()
    {
        var backend = new FlakyNotifications();
        var loop = new ManualEventLoop();
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };
        using var engine = new QmlEngine(StandardModules.Create(), loop, log);
        var listener = new NotificationListener(backend, monitor: true) { Engine = engine };
        listener.ComponentComplete();
        await backend.First.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (backend.Reads < 2 && DateTime.UtcNow < until)
        {
            await listener.RefreshAsync();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(backend.Reads >= 2, $"reads stayed at {backend.Reads}");
        loop.Process();
        Assert.Equal("Hello", Assert.IsType<ShellNotification>(Assert.Single(listener.Notifications.Rows)).Title);
        Assert.Contains(messages, m => m.Contains("center broke", StringComparison.Ordinal));
        Assert.False(listener.MonitorTask!.IsFaulted);
        Assert.False(listener.MonitorTask.IsCompleted);
        listener.Destroy();
        await listener.MonitorTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(listener.MonitorTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void Destroying_the_listener_ends_the_monitor_before_the_token_source_is_disposed()
    {
        var backend = new BlockingNotifications();
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var listener = new NotificationListener(backend, monitor: true) { Engine = engine };
        listener.ComponentComplete();
        // IsCompleted, not await: the signal completes on the monitor thread, and awaiting it queues a
        // continuation on the thread pool that a busy pool never ran.
        var untilStarted = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!backend.Started.Task.IsCompleted && DateTime.UtcNow < untilStarted)
            Thread.Sleep(10);
        Assert.True(backend.Started.Task.IsCompleted);

        var started = DateTime.UtcNow;
        listener.Destroy();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(1.5), $"destroy waited {elapsed}");
        var untilEnded = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (listener.MonitorTask is { IsCompleted: false } && DateTime.UtcNow < untilEnded)
            Thread.Sleep(10);
        Assert.True(listener.MonitorTask!.IsCompletedSuccessfully);
        Assert.True(backend.Cancelled);
    }

    private static BluetoothSnapshot ReadyBluetooth() =>
        new(true, new BluetoothAdapterInfo("adapter", "Bluetooth", "00:11:22:33:44:55", BluetoothAdapterState.On), []);

    private static NotificationSnapshot ReadyNotifications() =>
        new(NotificationAccessStatus.Allowed, [new IncomingToast(7, "Mail.App", "Mail", "Hello", "A message", DateTimeOffset.UnixEpoch)],
            NotificationState.Off, NotificationMode.Unrestricted);

    private sealed class FlakyBluetooth : IBluetoothBackend
    {
        private int _reads;
        public int Reads => _reads;
        public bool Disposed { get; private set; }
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            var n = Interlocked.Increment(ref _reads);
            if (n == 1)
            {
                First.TrySetResult();
                return Task.FromException<BluetoothSnapshot>(new InvalidCastException("radio broke"));
            }
            return Task.FromResult(ReadyBluetooth());
        }
        public void StartDiscovery() { }
        public void StopDiscovery() { }
        public Task<string?> PairAsync(string id, CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task<string?> UnpairAsync(string id) => Task.FromResult<string?>(null);
        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => Task.FromResult(true);
        public void Dispose() => Disposed = true;
    }

    private sealed class TokenBluetooth : IBluetoothBackend
    {
        private int _finished;
        public int Finished => Volatile.Read(ref _finished);
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            First.TrySetResult();
            cancellation.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _finished);
            return Task.FromResult(ReadyBluetooth());
        }
        public void StartDiscovery() { }
        public void StopDiscovery() { }
        public Task<string?> PairAsync(string id, CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task<string?> UnpairAsync(string id) => Task.FromResult<string?>(null);
        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => Task.FromResult(true);
        public void Dispose() { }
    }

    private sealed class GatedBluetooth : IBluetoothBackend
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Completing the gate resumes the read on this thread. Queuing that resume on the thread pool
        // left the refresh unfinished when the pool was busy.
        public TaskCompletionSource Gate { get; } = new();
        public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            Started.TrySetResult();
            await Gate.Task.WaitAsync(cancellation).ConfigureAwait(false);
            return ReadyBluetooth();
        }
        public void StartDiscovery() { }
        public void StopDiscovery() { }
        public Task<string?> PairAsync(string id, CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task<string?> UnpairAsync(string id) => Task.FromResult<string?>(null);
        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => Task.FromResult(true);
        public void Dispose() { }
    }

    private sealed class BlockingBluetooth : IBluetoothBackend
    {
        public bool Disposed { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
            return ReadyBluetooth();
        }
        public void StartDiscovery() { }
        public void StopDiscovery() { }
        public Task<string?> PairAsync(string id, CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task<string?> UnpairAsync(string id) => Task.FromResult<string?>(null);
        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => Task.FromResult(true);
        public void Dispose() => Disposed = true;
    }

    private sealed class FlakyNotifications : INotificationBackend
    {
        private int _reads;
        public int Reads => _reads;
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);
        public Task<NotificationSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation)
        {
            var n = Interlocked.Increment(ref _reads);
            if (n == 1)
            {
                First.TrySetResult();
                return Task.FromException<NotificationSnapshot>(new InvalidCastException("center broke"));
            }
            return Task.FromResult(ReadyNotifications());
        }
        public void Dismiss(uint id) { }
    }

    private sealed class BlockingNotifications : INotificationBackend
    {
        public bool Cancelled { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);
        public Task<NotificationSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public async Task<NotificationSnapshot> ReadAsync(CancellationToken cancellation)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
            return ReadyNotifications();
        }
        public void Dismiss(uint id) { }
    }
}
