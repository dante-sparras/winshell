using Windows.Foundation;
using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>
/// The media sessions Windows shows in its media flyout (the Global System Media Transport Controls), one per
/// app that plays media. One list serves the whole process, so a reload does not ask Windows again. It lives on
/// the UI thread; Windows' events arrive on other threads and are posted to it.
/// </summary>
internal sealed class SmtcSessions : IMediaSessions
{
    private static SmtcSessions? _instance;

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly TimeProvider _time;
    private readonly List<SmtcSession> _sessions = [];
    private readonly AppNames _names;
    private readonly SessionManagerPoll _poll;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    private TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs>?
        _sessionsChanged;

    private int _nextId = 1;

    internal SmtcSessions(IEventLoop loop, Log log,
        Func<IAsyncOperation<GlobalSystemMediaTransportControlsSessionManager>> request, TimeProvider time)
    {
        _loop = loop;
        _log = log;
        _time = time;
        _poll = new SessionManagerPoll(loop, log, request, manager =>
        {
            Subscribe(manager);
            Read(manager.GetSessions());
        });
        _names = new AppNames(loop, log, AppDisplayName.Find, _poll.Refresh);
        loop.Post(_poll.Refresh);
    }

    /// <summary>The session list, made on first use. Call on the UI thread.</summary>
    public static SmtcSessions Get(IEventLoop loop, Log log)
    {
        if (_instance is not null)
            return _instance;
        // Old art is older than a day, so cleaning it does not have to finish before the first read.
        _ = Task.Run(ArtCache.CleanOld);
        // The shell's clock. A session counts playback forward from it while playing.
        return _instance = new SmtcSessions(loop, log, GlobalSystemMediaTransportControlsSessionManager.RequestAsync,
            TimeProvider.System);
    }

    public IReadOnlyList<SmtcSession> Sessions => _sessions;

    public event Action<SmtcSession>? SessionAdded;

    public event Action<SmtcSession>? SessionRemoved;

    /// <summary>The projection does not root the delegate. A collected handler never hears a later session.</summary>
    private void Subscribe(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (ReferenceEquals(_manager, manager))
            return;
        if (_manager is not null && _sessionsChanged is not null)
            _manager.SessionsChanged -= _sessionsChanged;
        _manager = manager;
        _sessionsChanged ??= (_, _) => _loop.Post(_poll.Refresh);
        manager.SessionsChanged += _sessionsChanged;
    }

    private void Read(IReadOnlyList<GlobalSystemMediaTransportControlsSession> current)
    {
        // GetSessions returns a new object for a session that is already held, so reference equality
        // drops it and starts again. That cancels the read of its title and art. Match the app id instead.
        var incoming = current.ToList();
        var held = _sessions.ToList();
        var ids = incoming.Select(SmtcSession.IdOf).ToList();
        var pair = Pair(held.Select(s => s.AppId).ToList(), ids);
        var kept = new bool[held.Count];
        for (var i = 0; i < incoming.Count; i++)
        {
            var existing = pair[i];
            if (existing >= 0)
            {
                kept[existing] = true;
                held[existing].Retarget(incoming[i]);
                // The shell name arrives after the session. The UI thread does not wait for that lookup.
                held[existing].Rename(_names.Get(ids[i]));
                continue;
            }

            var session = new SmtcSession(_loop, _log, incoming[i], _nextId++, _names.Get(ids[i]), _time);
            _sessions.Add(session);
            SessionAdded?.Invoke(session);
            session.Start();
        }

        for (var i = 0; i < held.Count; i++)
        {
            if (kept[i])
                continue;
            _sessions.Remove(held[i]);
            held[i].Release();
            SessionRemoved?.Invoke(held[i]);
        }
    }

    /// <summary>
    /// Incoming index to held index, or -1 when that incoming session is new. The same app id pairs in
    /// list order, because one app can have more than one session and Windows does not give them an id.
    /// </summary>
    internal static int[] Pair(IReadOnlyList<string> heldAppIds, IReadOnlyList<string> incomingAppIds)
    {
        var pair = new int[incomingAppIds.Count];
        var used = new bool[heldAppIds.Count];
        for (var i = 0; i < incomingAppIds.Count; i++)
        {
            pair[i] = -1;
            for (var h = 0; h < heldAppIds.Count; h++)
            {
                if (used[h] || heldAppIds[h] != incomingAppIds[i])
                    continue;
                used[h] = true;
                pair[i] = h;
                break;
            }
        }

        return pair;
    }
}
