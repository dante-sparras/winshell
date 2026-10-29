using System.Text;
using Winshell.Runtime;

namespace Winshell;

/// <summary>
/// The instance's log file. Ordinary lines stay in memory and land on a timer, so a binding that logs does not
/// flush the disk on the caller. Warn and above flush at once, which is also what a crash report reads back.
/// Once the file reaches its cap it becomes <c>log.1.txt</c> and a new <c>log.txt</c> starts, so a shell that
/// runs for the whole session cannot fill the disk.
/// </summary>
internal sealed class InstanceLog : IDisposable
{
    internal const long DefaultCap = 4 * 1024 * 1024;

    private readonly string _path;
    private readonly long _cap;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private StreamWriter? _writer;
    private readonly StringBuilder _pending = new();
    private bool _closed;

    public InstanceLog(string path, long cap = DefaultCap, TimeSpan? flushEvery = null)
    {
        _path = path;
        _cap = cap;
        _writer = Open();
        var period = flushEvery ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => Flush(), null, period, period);
    }

    /// <summary>Queues one already-stamped line. Warn and above are on disk before this returns.</summary>
    public void Write(LogLevel level, string stampedLine)
    {
        lock (_gate)
        {
            if (_closed)
                return;
            _pending.Append(stampedLine).Append('\n');
            if (level >= LogLevel.Warn)
                FlushLocked();
        }
    }

    public void Flush()
    {
        lock (_gate)
            FlushLocked();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            FlushLocked();
            _writer?.Dispose();
            _writer = null;
        }
        _timer.Dispose();
    }

    private void FlushLocked()
    {
        if (_writer is null || _pending.Length == 0)
            return;
        if (_writer.BaseStream.Length >= _cap)
            Roll();
        _writer.Write(_pending.ToString());
        _writer.Flush();
        _pending.Clear();
    }

    private void Roll()
    {
        _writer!.Dispose();
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
