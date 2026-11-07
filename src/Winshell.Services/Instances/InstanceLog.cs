using System.Runtime.CompilerServices;
using System.Text;

namespace Winshell;

/// <summary>
/// The instance's log file. Ordinary lines stay in memory and land a moment later, so a binding that logs does not
/// flush the disk on the caller. The timer runs only while a line waits, so an idle shell is not woken for it. Warn
/// and above flush at once, which is also what a crash report reads back.
/// Once the file reaches its cap it is copied to <see cref="RolledName"/> and starts again empty, so a shell that
/// runs for the whole session cannot fill the disk. It stays the same file, so a reader that holds it open, such as
/// <see cref="Follow"/>, still reads what comes next.
/// </summary>
internal sealed class InstanceLog : IDisposable
{
    internal const long DefaultCap = 4 * 1024 * 1024;

    /// <summary>The log in an instance's folder.</summary>
    public const string FileName = "log.txt";

    /// <summary>The part before the last roll, beside <see cref="FileName"/>.</summary>
    public const string RolledName = "log.1.txt";

    private static readonly TimeSpan FollowPoll = TimeSpan.FromMilliseconds(200);

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
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => FlushFromTimer(), null, Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
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
                if (!waiting && _pending.Length > 0)
                    _timer.Change(_delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            FlushLocked();
        }
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
        var rolled = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, RolledName);
        try
        {
            File.Copy(_path, rolled, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A reader still has the previous roll. Keep appending; the next flush tries again.
            _writer = Open();
            return;
        }

        _writer = Open(FileMode.Create);
    }

    private StreamWriter Open(FileMode mode = FileMode.Append)
    {
        return new StreamWriter(new FileStream(_path, mode, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            AutoFlush = false
        };
    }

    /// <summary>Opens a log to read while its shell may still be writing and rolling it.</summary>
    public static FileStream OpenToRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    /// <summary>
    /// Each line written to the log after what <paramref name="reader"/> has read, until <paramref name="cancel"/>.
    /// A roll empties the file, and reading goes on from its start.
    /// </summary>
    public static async IAsyncEnumerable<string> Follow(StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var stream = reader.BaseStream;
        while (true)
        {
            if (await reader.ReadLineAsync(cancel) is { } line)
            {
                yield return line;
                continue;
            }

            // A shell writes far less than the cap between two reads, so a file shorter than what was read has rolled.
            // The roll copied it to log.1.txt first, so what was not read yet is there.
            if (stream.Length < stream.Position)
            {
                if (stream is FileStream { Name: var name })
                    foreach (var missed in Rest(Path.Combine(Path.GetDirectoryName(name)!, RolledName),
                                 stream.Position))
                        yield return missed;
                stream.Position = 0;
                reader.DiscardBufferedData();
                continue;
            }

            await Task.Delay(FollowPoll, cancel);
        }
    }

    /// <summary>The lines of <paramref name="file"/> after byte <paramref name="from"/>, or none when it cannot be read.</summary>
    private static List<string> Rest(string file, long from)
    {
        var lines = new List<string>();
        try
        {
            using var stream = OpenToRead(file);
            if (stream.Length <= from)
                return lines;
            stream.Position = from;
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                lines.Add(line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return lines;
    }
}
