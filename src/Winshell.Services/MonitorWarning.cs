
namespace Winshell;

/// <summary>
/// Warns about a failed poll once. The same failure is quiet until a poll succeeds or a different failure comes.
/// </summary>
internal sealed class MonitorWarning(Func<Log> log, string category)
{
    private string? _last;

    public void Failed(Exception e)
    {
        if (e.Message == _last)
            return;
        _last = e.Message;
        log().Warn(category, e.Message);
    }

    public void Succeeded() => _last = null;
}
