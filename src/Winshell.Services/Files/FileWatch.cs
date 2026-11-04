namespace Winshell;

/// <summary>
/// Follows one file that may not exist yet, and calls back on the loop when it may have changed. A folder that is
/// missing is watched from the nearest one that exists, and each folder created under it moves the watch one step
/// closer. Nothing is called after <see cref="Dispose"/>.
/// </summary>
public sealed class FileWatch : IDisposable
{
    private readonly string _path;
    private readonly IEventLoop _loop;
    private readonly Action _changed;
    private DirectoryWatch? _watch;
    private int _generation;
    private bool _disposed;

    /// <summary>
    /// Starts watching <paramref name="path"/>, calling <paramref name="changed"/> on <paramref name="loop"/>.
    /// </summary>
    /// <param name="path">The file to follow. A symbolic link is followed to its target.</param>
    /// <param name="loop">The loop <paramref name="changed"/> is called on.</param>
    /// <param name="changed">Called when the file may have changed. Read the file again to see whether it did.</param>
    public FileWatch(string path, IEventLoop loop, Action changed)
    {
        _path = path;
        _loop = loop;
        _changed = changed;
        Watch();
    }

    private void Watch()
    {
        var full = AtomicFile.FollowLink(_path);
        var dir = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        var parent = false;
        while (dir is not null && !Directory.Exists(dir))
        {
            parent = true;
            name = Path.GetFileName(dir);
            dir = Path.GetDirectoryName(dir);
        }
        if (dir is null)
            return;
        var generation = ++_generation;
        // Read on the loop, after the event. Reading here would hold the file during a replace.
        Action arrived = parent ? () => Rewatch(generation) : Report;
        _watch = DirectoryWatch.Start(dir, parent ? DirectoryWatch.NameChanges : DirectoryWatch.FileChanges, changedName =>
        {
            if (changedName.Equals(name, StringComparison.OrdinalIgnoreCase))
                _loop.Post(arrived);
        }, () => _loop.Post(() => Rewatch(generation)));
        // A folder made after the check above and before the watch started will not be reported.
        if (_watch is not null && parent && Directory.Exists(Path.Combine(dir, name)))
            _loop.Post(arrived);
    }

    /// <summary>Watches again after a watched folder appeared or the watch ended.</summary>
    private void Rewatch(int generation)
    {
        if (_disposed || generation != _generation)
            return;
        _watch?.Dispose();
        _watch = null;
        Watch();
        // The file may have been written before this reached the loop, so the new watch would not see it.
        _changed();
    }

    private void Report()
    {
        if (!_disposed)
            _changed();
    }

    /// <summary>Stops watching the file.</summary>
    public void Dispose()
    {
        _disposed = true;
        _watch?.Dispose();
        _watch = null;
    }
}
