using System.Text.Json;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The apps pinned to the taskbar, kept in <c>taskbar.json</c> in the shell's data folder. Until you pin, unpin or
/// reorder something, the taskbar has the apps Windows 11 pins: File Explorer, Microsoft Edge and the Microsoft Store.
/// Each entry is an app id from the app index, or the path of a program it does not have. An empty path keeps them
/// for this run only.
/// </summary>
public sealed class TaskbarPins : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<string> Defaults = ["Microsoft.Windows.Explorer", "MSEdge", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"];

    private readonly string _path;
    private readonly Log _log;
    private readonly FileWatch? _watch;
    private IReadOnlyList<string> _ids = Defaults;

    public TaskbarPins(string path, IEventLoop loop, Log? log = null)
    {
        _path = path;
        _log = log ?? Log.Default;
        // Without a path the pins are not kept at all, and there is nothing to watch.
        if (path.Length == 0)
        {
            _watch = null;
            return;
        }
        _watch = new FileWatch(path, loop, Read);
        Read();
    }

    public IReadOnlyList<string> Ids
    {
        get => _ids;
        private set
        {
            // A read of our own write is a new list of the same ids, and that is not a change.
            if (_ids.SequenceEqual(value))
                return;
            Set(ref _ids, value);
        }
    }

    public void Pin(string id)
    {
        if (id.Length > 0 && !Ids.Contains(id))
            Write([.. Ids, id]);
    }

    public void Unpin(string id)
    {
        if (!Ids.Contains(id))
            return;
        Write([.. Ids.Where(pinned => pinned != id)]);
    }

    /// <summary>Puts this pin where another pin is: before it when moving back, after it when moving on.</summary>
    public void Move(string id, string target)
    {
        var at = Ids.ToList().IndexOf(target);
        if (id.Length == 0 || id == target || at < 0 || !Ids.Contains(id))
            return;
        var order = Ids.Where(pinned => pinned != id).ToList();
        order.Insert(at, id);
        Write(order);
    }

    private void Read()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.TryGetProperty("pinned", out var pinned) && pinned.ValueKind == JsonValueKind.Array)
                Ids = [.. pinned.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Warn("taskbar", $"Could not read {_path}: {e.Message}");
        }
    }

    private void Write(IReadOnlyList<string> ids)
    {
        Ids = ids;
        if (_path.Length == 0)
            return;
        // A temp file beside the real one, then a replace, so a crash cannot leave half a taskbar.json.
        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, Path.GetRandomFileName());
            File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, IReadOnlyList<string>> { ["pinned"] = ids }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warn("taskbar", $"Could not write {_path}: {e.Message}");
            if (temp is null)
                return;
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose() => _watch?.Dispose();
}
