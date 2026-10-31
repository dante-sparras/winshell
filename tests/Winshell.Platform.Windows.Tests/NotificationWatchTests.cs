using Winshell.Platform.Windows.Services.Notifications;

namespace Winshell.Platform.Windows.Tests;

public sealed class NotificationWatchTests
{
    [Fact]
    public async Task A_toast_change_refreshes_without_waiting_for_the_poll_and_destroy_stops_the_watch()
    {
        var backend = new WatchingBackend();
        var listener = new NotificationListener(backend, monitor: true);
        try
        {
            listener.ComponentComplete();
            await backend.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(backend.Changed);
            backend.Changed();
            await backend.SecondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            listener.Destroy();
        }

        await backend.Unwatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(backend.Changed);
    }

    private sealed class WatchingBackend : INotificationBackend
    {
        private int _reads;
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Unwatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? Changed { get; private set; }

        public Action WatchToasts(Action changed)
        {
            Changed = changed;
            return () =>
            {
                Changed = null;
                Unwatched.TrySetResult();
            };
        }

        public Task<NotificationSnapshot> ReadAsync()
        {
            (Interlocked.Increment(ref _reads) == 1 ? FirstRead : SecondRead).TrySetResult();
            return Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Off, NotificationMode.Unrestricted));
        }

        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);
        public void Dismiss(uint id) { }
    }
}
