namespace Winshell;

/// <summary>
/// Warns about a failed poll once. The same failure is quiet until a poll succeeds or a different failure comes.
/// </summary>
/// <param name="log">Where the warning goes.</param>
/// <param name="category">The log category.</param>
/// <param name="what">What failed, put before the failure's message; empty for the message alone.</param>
internal sealed class MonitorWarning(Log log, string category, string what = "")
{
    private string? _last;

    public void Failed(Exception e)
    {
        if (e.Message == _last)
            return;
        _last = e.Message;
        log.Warn(category, what.Length == 0 ? e.Message : $"{what}: {e.Message}");
    }

    public void Succeeded()
    {
        _last = null;
    }
}
