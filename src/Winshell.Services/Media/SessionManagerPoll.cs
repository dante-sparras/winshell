using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>
/// Asks Windows for its media session manager over and over, and hands each one it gets to <c>read</c> on the UI
/// thread: a manager requested on another thread can keep returning an empty list. Each pending request holds a
/// native thread, so only one is out at a time, until it completes or times out.
/// </summary>
internal sealed class SessionManagerPoll
{
    /// <summary>SessionsChanged can miss a session, so a completed read is followed by another after this delay.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>A request still pending after this long is cancelled and replaced, so a broker that stopped answering and recovered is asked again.</summary>
    private static readonly TimeSpan FirstRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cancelling a hung request does not free the native thread it holds, so each timeout doubles the next wait, up to
    /// this. A broker that never answers then costs a thread an hour, not one every half minute.
    /// </summary>
    private static readonly TimeSpan LongestRequestTimeout = TimeSpan.FromHours(1);

    /// <summary>
    /// Logged once when a request times out, so a broker that stopped answering does not look like nothing playing. The
    /// broker is Windows' Now Playing Session Manager service; every app's request hangs while it is stuck.
    /// </summary>
    private const string NotAnswering =
        "Windows' media session service (Now Playing Session Manager) is not answering, so no media session can be listed. Signing out, or restarting that service, brings it back.";

    private readonly IEventLoop _loop;
    private readonly Func<IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager>> _request;
    private readonly Action<GlobalSystemMediaTransportControlsSessionManager> _read;
    private readonly MonitorWarning _warning;
    private IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager>? _pending;
    private IDisposable? _refresh;
    private IDisposable? _timeout;
    private TimeSpan _requestTimeout = FirstRequestTimeout;
    private bool _refreshRequested;

    public SessionManagerPoll(IEventLoop loop, Log log,
        Func<IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager>> request,
        Action<GlobalSystemMediaTransportControlsSessionManager> read)
    {
        _loop = loop;
        _request = request;
        _read = read;
        _warning = new MonitorWarning(log, "media");
    }

    /// <summary>Asks Windows now, or once the request that is out finishes. Changes while it is out share that one follow-up.</summary>
    public void Refresh()
    {
        if (_pending is not null)
        {
            _refreshRequested = true;
            return;
        }

        _refresh?.Dispose();
        _refresh = null;
        try
        {
            var operation = _request();
            _pending = operation;
            _timeout = _loop.Schedule(_requestTimeout, () => CancelExpired(operation));
            operation.Completed = (_, status) => _loop.Post(() => Complete(operation, status));
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            _warning.Failed(e);
            // A request that started is retired by its timeout.
            if (_pending is null)
                ScheduleRefresh();
        }
    }

    private void CancelExpired(IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager> operation)
    {
        if (!ReferenceEquals(_pending, operation))
            return;
        _warning.Failed(new TimeoutException(NotAnswering));
        try
        {
            operation.Cancel();
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            _warning.Failed(e);
        }

        _requestTimeout = _requestTimeout * 2 < LongestRequestTimeout ? _requestTimeout * 2 : LongestRequestTimeout;
        // A hung broker may never deliver Completed after Cancel, so retire the request here; its late callback is ignored.
        Complete(operation, AsyncStatus.Canceled);
    }

    private void Complete(IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager> operation,
        AsyncStatus status)
    {
        if (!ReferenceEquals(_pending, operation))
            return;
        _timeout?.Dispose();
        _timeout = null;
        // Only a timeout cancels a request, so any other status means the broker answered.
        if (status != AsyncStatus.Canceled)
            _requestTimeout = FirstRequestTimeout;
        try
        {
            if (status == AsyncStatus.Completed)
            {
                _read(operation.GetResults());
                _warning.Succeeded();
            }
            else if (status == AsyncStatus.Error)
            {
                _warning.Failed(operation.ErrorCode);
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            _warning.Failed(e);
        }
        finally
        {
            try
            {
                operation.Close();
            }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                _warning.Failed(e);
            }

            _pending = null;
            if (_refreshRequested)
            {
                _refreshRequested = false;
                _loop.Post(Refresh);
            }
            else
            {
                ScheduleRefresh();
            }
        }
    }

    private void ScheduleRefresh()
    {
        _refresh = _loop.Schedule(RefreshInterval, Refresh);
    }
}
