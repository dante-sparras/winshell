using Winshell.Platform.Windows.Services;
using Winshell.Platform.Windows.Services.Bluetooth;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

public class MonitorCancellationTests
{
    [Fact]
    public async Task Destroy_does_not_wait_for_a_read_that_ignores_cancellation_and_disposes_after_it()
    {
        var backend = new StubbornBluetooth();
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var service = new BluetoothService(engine, backend);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var started = DateTime.UtcNow;
        service.Destroy();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(500), $"destroy waited {elapsed}");
        Assert.False(backend.Disposed);
        backend.Finish.TrySetResult(ReadyBluetooth());
        await service.MonitorTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(backend.Disposed);
        Assert.False(backend.DisposedDuringRead);
    }

    [Fact]
    public async Task A_manual_refresh_also_finishes_before_the_backend_is_disposed()
    {
        var backend = new StubbornBluetooth();
        var loop = new ManualEventLoop();
        var service = new BluetoothService(backend, loop);
        var read = service.RefreshAsync();
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        service.Destroy();
        Assert.False(backend.Disposed);
        backend.Finish.TrySetResult(ReadyBluetooth());
        await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        loop.Process();

        Assert.True(backend.Disposed);
        Assert.False(backend.DisposedDuringRead);
        Assert.False(service.Ready);
    }

    private static BluetoothSnapshot ReadyBluetooth() =>
        new(true, new BluetoothAdapterInfo("adapter", "Bluetooth", "00:11:22:33:44:55", BluetoothAdapterState.On), []);

    private sealed class StubbornBluetooth : IBluetoothBackend
    {
        private bool _reading;
        public bool Disposed { get; private set; }
        public bool DisposedDuringRead { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<BluetoothSnapshot> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BluetoothSnapshot> ReadAsync() => ReadAsync(CancellationToken.None);
        public async Task<BluetoothSnapshot> ReadAsync(CancellationToken cancellation)
        {
            // A Windows call that does not honor cancellation.
            _reading = true;
            Started.TrySetResult();
            var snapshot = await Finish.Task.ConfigureAwait(false);
            _reading = false;
            return snapshot;
        }
        public void StartDiscovery() { }
        public void StopDiscovery() { }
        public Task<string?> PairAsync(string id, CancellationToken cancellation) => Task.FromResult<string?>(null);
        public Task<string?> UnpairAsync(string id) => Task.FromResult<string?>(null);
        public Task<bool> SetAudioConnectionAsync(Guid container, bool connect) => Task.FromResult(true);
        public void Dispose()
        {
            DisposedDuringRead |= _reading;
            Disposed = true;
        }
    }

}
