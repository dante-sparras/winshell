using System.Text;

namespace Winshell;

/// <summary>
/// The instance's log file. Ordinary lines stay in memory and land a moment later, so a binding that logs does not
/// flush the disk on the caller. The timer runs only while a line waits, so an idle shell is not woken for it. Warn
/// and above flush at once, which is also what a crash report reads back.
/// Once the file reaches its cap it becomes <c>log.1.txt</c> and a new <c>log.txt</c> starts, so a shell that
/// runs for the whole session cannot fill the disk.
/// </summary>
internal sealed class InstanceLog : IDisposable
{
    internal const long DefaultCap = 4 * 1024 * 1024;

    private readonly string _path;
    private readonly long _cap;
    private readonly object _gate = new();
    private readonly TimeSpan _delay;
    private readonly ITimer _timer;
    private StreamWriter? _writer;
    private readonly StringBuilder _pending = new();
    private bool _closed;

    public InstanceLog(string path, long cap = DefaultCap, TimeSpan? flushAfter = null, TimeProvider? time = null)
    {
        _path = path;
        _cap = cap;
        _writer = Open();
        _delay = flushAfter ?? TimeSpan.FromMilliseconds(500);
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => FlushFromTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Queues one already-stamped line. Warn and above are on disk before this returns.</summary>
    public void Write(LogLevel level, string stampedLine)
    {
        lock (_gate)
        {
            if (_closed)
                return;
            var waiting = _pending.Length > 0;
            _pending.Append(stampedLine).Append('\n');
            try
            {
                if (level >= LogLevel.Warn)
                    FlushLocked();
            }
            finally
            {
                // The first line to wait starts the timer, as does a warning whose flush failed.
                if (!waiting && _pending.Length > 0)
                    _timer.Change(_delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Flush()
    {
        lock (_gate)
            FlushLocked();
    }

    // Nobody awaits the timer, so a failure here would end the process. The lines that could not land are dropped,
    // which also lets the next Write start the timer again.
    private void FlushFromTimer()
    {
        lock (_gate)
        {
            try
            {
                FlushLocked();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _pending.Clear();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
                return;
            try
            {
                FlushLocked();
            }
            finally
            {
                _closed = true;
                _writer?.Dispose();
                _writer = null;
            }
        }
        _timer.Dispose();
    }

    private void FlushLocked()
    {
        if (_closed || _pending.Length == 0)
            return;
        // A roll whose reopen failed left no writer; the next flush opens one again.
        _writer ??= Open();
        if (_writer.BaseStream.Length >= _cap)
            Roll();
        _writer.Write(_pending.ToString());
        _writer.Flush();
        _pending.Clear();
    }

    private void Roll()
    {
        _writer!.Dispose();
        _writer = null;
        var rolled = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, "log.1.txt");
        try
        {
            File.Move(_path, rolled, overwrite: true);
        }
        catch (IOException)
        {
            // A reader still has the previous roll. Keep appending; the next flush tries again.
        }
        _writer = Open();
    }

    private StreamWriter Open() => new(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
    {
        AutoFlush = false,
    };
}
