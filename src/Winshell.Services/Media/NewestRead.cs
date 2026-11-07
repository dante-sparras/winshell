namespace Winshell.Media;

/// <summary>
/// Numbers a run of reads so only the newest one's result is applied. A read can start on any thread and ends on
/// the UI thread.
/// </summary>
internal sealed class NewestRead
{
    private int _started;
    private int _ended;

    /// <summary>The newest read has not ended. Starting another drops its result.</summary>
    public bool Running => _ended != _started;

    public int Start()
    {
        return Interlocked.Increment(ref _started);
    }

    /// <summary>Ends a read, and says whether it is the newest, so its result is applied.</summary>
    public bool End(int read)
    {
        if (read != _started)
            return false;
        _ended = read;
        return true;
    }
}
