using System.Text.Json;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The apps pinned to the taskbar, kept in <c>taskbar.json</c> in the shell's data folder. Until you pin, unpin or
/// reorder something, the taskbar has the apps Windows 11 pins: File Explorer, Microsoft Edge and the Microsoft Store.
/// Each entry is an app id from the app index, or the path of a program it does not have.
/// </summary>
public sealed class TaskbarPins : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<string> Defaults = ["Microsoft.Windows.Explorer", "MSEdge", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"];

    private readonly string _path;
    private readonly Log _log;
    private readonly FileWatch _watch;
    private IReadOnlyList<string> _ids = Defaults;

    public TaskbarPins(string path, IEventLoop loop, Log? log = null)
    {
        _path = path;
        _log = log ?? Log.Default;
        _watch = new FileWatch(path, loop, Read);
        Read();
    }

    public IReadOnlyList<string> Ids
    {
        get => _ids;
        private set
        {
            // A read of our own write is a new list of the same ids, and that is not a change.
            if (!_ids.SequenceEqual(value))
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
        if (Ids.Contains(id))
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
        // A temp file beside the real one, then a replace, so a crash cannot leave half a taskbar.json.
        var temp = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, IReadOnlyList<string>> { ["pinned"] = ids }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warn("taskbar", $"Could not write {_path}: {e.Message}");
        }
    }

    public void Dispose() => _watch.Dispose();
}
