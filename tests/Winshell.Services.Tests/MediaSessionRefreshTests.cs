using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media.Control;
using Winshell.Media;

namespace Winshell.Services.Tests;

public sealed class MediaSessionRefreshTests
{
    [Fact]
    public void A_pending_discovery_waits_for_Windows_without_polling_or_starting_more_requests()
    {
        var loop = new ManualEventLoop();
        var requests = 0;
        var pending = new Discovery();
        _ = new SmtcSessions(loop, new Log(), () => ++requests == 1 ? pending : new Discovery());

        loop.Process();
        loop.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, requests);
        Assert.Null(loop.NextDue);
        Assert.False(pending.Closed);
    }

    [Theory]
    [InlineData(AsyncStatus.Completed)]
    [InlineData(AsyncStatus.Error)]
    [InlineData(AsyncStatus.Canceled)]
    public void A_finished_discovery_releases_its_resources_and_retries_after_two_seconds(AsyncStatus status)
    {
        var loop = new ManualEventLoop();
        var requests = 0;
        var first = new Discovery();
        var next = new Discovery();
        _ = new SmtcSessions(loop, new Log(), () => ++requests == 1 ? first : next);
        loop.Process();

        first.Finish(status);
        loop.Process();

        Assert.True(first.Closed);
        loop.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(1, requests);
        loop.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, requests);
        Assert.False(next.Closed);
    }

    [Fact]
    public void A_request_that_cannot_start_is_retried_after_two_seconds()
    {
        var loop = new ManualEventLoop();
        var requests = 0;
        var pending = new Discovery();
        _ = new SmtcSessions(loop, new Log(), () => ++requests == 1
            ? throw new COMException("The media broker is unavailable.")
            : pending);

        loop.Process();
        loop.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(1, requests);
        loop.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(2, requests);
        Assert.Null(loop.NextDue);
    }

    private sealed class Discovery : IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager>
    {
        private AsyncOperationCompletedHandler<GlobalSystemMediaTransportControlsSessionManager>? _completed;

        public AsyncOperationCompletedHandler<GlobalSystemMediaTransportControlsSessionManager> Completed
        {
            get => _completed ?? throw new InvalidOperationException("No completion handler.");
            set
            {
                _completed = value;
                if (Status != AsyncStatus.Started)
                    value(this, Status);
            }
        }

        public Exception ErrorCode { get; } = new COMException("The media broker is unavailable.");

        public uint Id => 1;

        public AsyncStatus Status { get; private set; } = AsyncStatus.Started;

        public bool Closed { get; private set; }

        // A completed request can lose its broker before its result is read.
        public GlobalSystemMediaTransportControlsSessionManager GetResults() => throw ErrorCode;

        public void Cancel() => Finish(AsyncStatus.Canceled);

        public void Close() => Closed = true;

        public void Finish(AsyncStatus status)
        {
            Status = status;
            Completed(this, status);
        }
    }
}
