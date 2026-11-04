using Windows.Foundation;
using Windows.Media.Control;
using Winshell.Media;

namespace Winshell.DefaultShell.Tests;

/// <summary>Stands in for Windows behind a media session. The commands the session sends land in <see cref="Sent"/>.</summary>
internal sealed class RecordingSmtcLink : ISmtcLink
{
    public List<string> Sent { get; } = [];

    public static SmtcSession Session(string appId, out RecordingSmtcLink link)
    {
        var recorder = link = new RecordingSmtcLink();
        return new SmtcSession(new ManualEventLoop(), new Log(), 0, appId, appId, _ => recorder);
    }

    public void Start()
    {
    }

    public void Retarget(GlobalSystemMediaTransportControlsSession session)
    {
    }

    public void Release()
    {
    }

    public void Send(string what, Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command) => Sent.Add(what);
}
