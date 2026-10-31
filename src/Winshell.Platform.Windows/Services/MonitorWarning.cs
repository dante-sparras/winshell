using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services;

/// <summary>
/// Warns about a failed poll once. The same failure is quiet until a poll succeeds or a different failure comes.
/// </summary>
internal sealed class MonitorWarning(QObject service, string category)
{
    private string? _last;

    public void Failed(Exception e)
    {
        if (e.Message == _last)
            return;
        _last = e.Message;
        ServiceSupport.Warn(service, category, e.Message);
    }

    public void Succeeded() => _last = null;
}
