using Winshell.Native;

namespace Winshell;

/// <summary>
/// Watches one folder on a thread of its own. FileSystemWatcher delivers on thread pool workers, so a busy pool
/// holds a change back. The first read starts before <see cref="Start"/> returns, so a change made after that is
/// not missed. That thread makes every read, because Windows cancels a read when the thread that made it ends.
/// Callbacks run on the watch thread and must not block.
/// </summary>
internal sealed class DirectoryWatch : IDisposable
{
    // FILE_NOTIFY_CHANGE_FILE_NAME | SIZE | LAST_WRITE.
    public const uint FileChanges = 0x00000001 | 0x00000008 | 0x00000010;
    // FILE_NOTIFY_CHANGE_FILE_NAME | DIR_NAME.
    public const uint NameChanges = 0x00000001 | 0x00000002;

    private readonly DirectoryChanges _changes;
    private readonly Action<string> _changed;
    private readonly Action _lost;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;
    private bool _reading;
    private volatile bool _stopped;
    private int _disposed;

    private DirectoryWatch(DirectoryChanges changes, Action<string> changed, Action lost)
    {
        _changes = changes;
        _changed = changed;
        _lost = lost;
        _thread = new Thread(Run) { IsBackground = true, Name = "Directory watch" };
    }

    /// <summary>Starts watching, or returns null when the folder can't be opened.</summary>
    /// <param name="path">The folder.</param>
    /// <param name="filter"><see cref="FileChanges"/> or <see cref="NameChanges"/>.</param>
    /// <param name="changed">Called with the name of each thing that changed, relative to the folder.</param>
    /// <param name="lost">Called once when the folder went away or changes were dropped. Nothing is reported after it.</param>
    /// <param name="subtree">Also report changes in folders under <paramref name="path"/>.</param>
    public static DirectoryWatch? Start(string path, uint filter, Action<string> changed, Action lost, bool subtree = false)
    {
        if (DirectoryChanges.Open(path, filter, subtree) is not { } changes)
            return null;
        var watch = new DirectoryWatch(changes, changed, lost);
        watch._thread.Start();
        // A handoff, not a stall: the new thread only makes the first read, which returns at once.
        watch._started.Wait();
        if (!watch._reading)
        {
            watch.Dispose();
            return null;
        }
        return watch;
    }

    private void Run()
    {
        _reading = _changes.Read();
        _started.Set();
        if (!_reading)
            return;
        var names = new List<string>();
        while (true)
        {
            names.Clear();
            var ok = _changes.Wait(names);
            if (_stopped)
                return;
            // A failed read is a folder that went away. An empty one is a buffer that overflowed.
            if (!ok)
            {
                _lost();
                return;
            }
            foreach (var name in names)
                _changed(name);
            if (!_changes.Read())
            {
                _lost();
                return;
            }
        }
    }

    public void Dispose()
    {
        // A second call would cancel through a closed handle.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stopped = true;
        if (_thread.ThreadState != ThreadState.Unstarted)
        {
            // Cancel until the thread ends. It may be between reads, where one cancel would miss it.
            do
            {
                _changes.Cancel();
            }
            while (!_thread.Join(1));
        }
        _changes.Dispose();
        _started.Dispose();
    }
}
