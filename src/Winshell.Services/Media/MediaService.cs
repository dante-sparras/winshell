using System.Collections.ObjectModel;
using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>The media sessions Windows shows in the media controls, one for each app that plays media.</summary>
public sealed class MediaService : ObservableObject, IDisposable
{
    private readonly IMediaSessions _sessions;
    private readonly IEventLoop _loop;
    private readonly Log _log;

    internal MediaService(IEventLoop loop, Log log)
        : this(loop, log, new SmtcSessions(loop, log, GlobalSystemMediaTransportControlsSessionManager.RequestAsync))
    {
        TempImageStore.Shared.CleanOldOnce(failure => log.Warn("images", failure));
    }

    internal MediaService(IEventLoop loop, Log log, IMediaSessions sessions)
    {
        _loop = loop;
        _log = log;
        _sessions = sessions;
        Refresh(null);
        _sessions.SessionAdded += Refresh;
        _sessions.SessionRemoved += Refresh;
    }

    /// <summary>One session for each app in the Windows media controls. Empty when no app plays media.</summary>
    public ObservableCollection<MediaSession> Sessions { get; } = [];

    private void Refresh(SmtcSession? _)
    {
        Sessions.Sync(_sessions.Sessions, s => s.Native, n => n, n => new MediaSession(n, _loop, _log),
            static (_, _) => { }, s => s.Detach());
    }

    /// <summary>Stops following the Windows media sessions. The ones in <see cref="Sessions"/> stop updating.</summary>
    public void Dispose()
    {
        _sessions.SessionAdded -= Refresh;
        _sessions.SessionRemoved -= Refresh;
        foreach (var session in Sessions)
            session.Detach();
        _sessions.Dispose();
    }
}
