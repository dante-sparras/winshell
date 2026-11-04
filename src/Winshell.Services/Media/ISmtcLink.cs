using Windows.Foundation;
using Windows.Media.Control;

namespace Winshell.Media;

/// <summary>What a <see cref="SmtcSession"/> uses to reach Windows' session. A test gives it one that records commands.</summary>
internal interface ISmtcLink
{
    void Start();

    /// <summary>Follows the new object Windows returned for this same session.</summary>
    void Retarget(GlobalSystemMediaTransportControlsSession session);

    void Release();

    /// <summary>Sends a command; Windows' events bring the result.</summary>
    void Send(string what, Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command);
}
