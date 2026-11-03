using System.Collections.ObjectModel;

namespace Winshell.Media;

/// <summary>The media sessions Windows shows in the media controls, one for each app that plays media.</summary>
public sealed class MediaService : ObservableObject, IDisposable
{
    private readonly IMediaSessions _sessions;
    private readonly Log _log;

    internal MediaService(IEventLoop loop, Log log, IMediaSessions? sessions = null)
    {
        _log = log;
        _sessions = sessions ?? SmtcSessions.Get(loop, log);
        Refresh(null);
        _sessions.SessionAdded += Refresh;
        _sessions.SessionRemoved += Refresh;
    }

    public ObservableCollection<MediaSession> Sessions { get; } = [];

    private void Refresh(SmtcSession? _) =>
        Sessions.Sync(_sessions.Sessions, s => s.Native, n => n, n => new MediaSession(n, _log), static (_, _) => { }, s => s.Detach());

    public void Dispose()
    {
        _sessions.SessionAdded -= Refresh;
        _sessions.SessionRemoved -= Refresh;
        foreach (var session in Sessions)
            session.Detach();
    }
}
