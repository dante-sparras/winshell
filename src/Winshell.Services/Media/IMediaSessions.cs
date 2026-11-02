namespace Winshell.Media;

/// <summary>The media sessions Windows knows. A test gives <see cref="MediaService"/> its own.</summary>
internal interface IMediaSessions
{
    IReadOnlyList<SmtcSession> Sessions { get; }

    event Action<SmtcSession>? SessionAdded;

    event Action<SmtcSession>? SessionRemoved;
}
