using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Winshell;

/// <summary>The comment written above a key the file does not have yet. A line break starts another comment line.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TomlCommentAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>
/// A TOML file a user can edit by hand, as an object. A type derives from this and has one public read-write property
/// per key, the key being the property's name with a lowercase first letter. Its type is string, bool, int, long,
/// double, <see cref="IReadOnlyDictionary{TKey, TValue}"/> of string and object for a table,
/// <see cref="IReadOnlyList{T}"/> of any of those for an array or an array of tables, or object for anything. A property
/// that holds null until the file has the key is nullable.
/// <code>
/// public sealed class Layout(string path, IEventLoop loop) : TomlFile(path, loop)
/// {
///     [TomlComment("The tiles, in order.")]
///     public IReadOnlyList&lt;string&gt;? Pinned { get; set => Set(ref field, value); }
/// }
/// </code>
/// The file is read in the constructor, so a default belongs in the property's initializer. After that the object
/// follows the file, and assigning a property writes it, on the loop that was passed in. Everything else in the file
/// stays, as <see cref="TomlDocument"/> says. A write reads the file again and puts only assigned values into it, and
/// the keys it does not have yet. When that read fails or does not parse, nothing is written and a warning is logged.
/// </summary>
public abstract class TomlFile : ObservableObject, IDisposable
{
    private readonly string _path;
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly Func<string, object, string>? _check;
    private readonly List<Key> _keys;

    /// <summary>Keys assigned since the last read that parsed and the write they are waiting for.</summary>
    private readonly HashSet<Key> _assigned = [];

    private readonly FileWatch _watch;

    /// <summary>The text last read or written, so the watch can tell a hand save from this object's own write.</summary>
    private string? _text;

    private string _error = "";
    private bool _loaded;
    private bool _loading;
    private bool _writePending;
    private bool _disposed;

    /// <param name="path">The file. It and its folder need not exist yet.</param>
    /// <param name="loop">Where a change to the file arrives and a write runs. Assign properties on this loop.</param>
    /// <param name="log">Takes the warnings. The default is <see cref="Log.Default"/>.</param>
    /// <param name="check">
    /// Called with a key and the value read for it. It returns why the value is refused, or <c>""</c>. A refused value
    /// is reported and left in the file, and the one in use stays.
    /// </param>
    protected TomlFile(string path, IEventLoop loop, Log? log = null, Func<string, object, string>? check = null)
    {
        _path = Path.GetFullPath(path);
        _loop = loop;
        _log = log ?? Log.Default;
        _check = check;
        _keys = [.. GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken)
            .Select(p => new Key(p))];
        PropertyChanged += OnPropertyChanged;
        // Watching first, so a save between the two is not missed.
        _watch = new FileWatch(_path, loop, Follow);
        Follow();
    }

    /// <summary>Why the file could not be read, as <c>path[line:column]: message</c>, or empty when the last read parsed.</summary>
    public string Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>Whether the file is there and parses. Without it the properties keep the values they had.</summary>
    public bool Loaded
    {
        get => _loaded;
        private set => Set(ref _loaded, value);
    }

    /// <summary>Bytes a read will take. Past this, the file is left as it is. 1 MiB.</summary>
    internal int MaxFileBytes { get; set; } = DataLimits.MaxTextBytes;

    /// <summary>Tests only. Runs after the new text is ready and before it replaces the file, so a test can land a hand save.</summary>
    internal Action? BeforeReplace { get; set; }

    /// <summary>Writes the file now, adding the keys it does not have, or making it and its folder when there is none.</summary>
    public void Save() => Write();

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || _disposed || _keys.Find(key => key.Property.Name == e.PropertyName) is not { } key)
            return;
        _assigned.Add(key);
        PostWrite();
    }

    private void PostWrite()
    {
        if (_writePending)
            return;
        _writePending = true;
        _loop.Post(() =>
        {
            if (_writePending)
                Write();
        });
    }

    /// <summary>Takes in the file as it is now. A good read replaces what was assigned and could not be written.</summary>
    private void Follow()
    {
        // The write on its way reads the file too, and keeps what was assigned.
        if (_writePending)
            return;
        var (text, _, problem) = ReadFile();
        if (problem.Length > 0)
        {
            // A file past the limit is still the user's file. Any other failed read says nothing about it: an editor
            // can hold the file while it saves.
            if (!DataLimits.IsFileLimit(problem))
            {
                _log.Debug("io", $"TomlFile: failed to read {_path}: {problem}");
                return;
            }
            _text = null;
            Error = $"{_path}: {problem}";
            Loaded = false;
            Warn($"failed to read {Error}");
            return;
        }
        if (text is null)
        {
            _text = null;
            Error = "";
            Loaded = false;
            return;
        }
        if (text == _text)
            return;
        _text = text;
        if (!TomlDocument.TryParse(_path, text, out var document, out var error))
        {
            Error = error;
            Loaded = false;
            Warn($"failed to parse {error}");
            return;
        }
        Error = "";
        Apply(document, keepAssigned: false);
        _assigned.Clear();
        Loaded = true;
    }

    private void Write()
    {
        _writePending = false;
        var (text, bom, problem) = ReadFile();
        var error = $"{_path}: {problem}";
        TomlDocument? document = null;
        if (problem.Length == 0)
            TomlDocument.TryParse(_path, text ?? "", out document, out error);
        if (document is null)
        {
            Error = error;
            Loaded = false;
            Warn($"failed to parse {error}; the file was left unchanged");
            return;
        }
        Error = "";
        // A file that has not changed was applied already, and its refused values reported.
        if (text != _text)
            Apply(document, keepAssigned: true);
        var read = document.Values;
        foreach (var key in _keys)
        {
            var value = key.Property.GetValue(this);
            if (_assigned.Contains(key) || (value is not null && !read.ContainsKey(key.Name)))
                document.Set(key.Name, value, key.Comment);
        }
        var next = document.ToString();
        if (next != text && !Put(next, text, bom))
            return;
        _text = next;
        _assigned.Clear();
        Loaded = true;
    }

    /// <summary>Puts the text down, or returns false with the assigned values still waiting.</summary>
    private bool Put(string next, string? read, bool bom)
    {
        try
        {
            var destination = AtomicFile.SaveTarget(_path, out var hardLink);
            if (Path.GetDirectoryName(destination) is { } folder)
                Directory.CreateDirectory(folder);
            AtomicFile.DeleteStaleTemps(destination);
            var written = AtomicFile.Write(destination, next, bom, hardLink, () =>
            {
                BeforeReplace?.Invoke();
                return AtomicFile.HandSaveWins(ReadFile().Text, read, next);
            });
            // A hand save landed during this write. The next pass reads it and writes the assigned values into it.
            if (!written)
                PostWrite();
            return written;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn($"failed to write {_path}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads the values of <paramref name="document"/> into the properties. One that does not fit its property, or is
    /// refused, is reported and left: nothing writes a key that was not assigned, so the file keeps it as it is.
    /// </summary>
    private void Apply(TomlDocument document, bool keepAssigned)
    {
        _loading = true;
        try
        {
            foreach (var key in _keys)
            {
                if ((keepAssigned && _assigned.Contains(key)) || !document.Values.TryGetValue(key.Name, out var value))
                    continue;
                if (!TryConvert(value!, key.Property.PropertyType, out var converted))
                    Leave(key, "is the wrong type");
                else if (_check?.Invoke(key.Name, converted) is { Length: > 0 } reason)
                    Leave(key, reason);
                else if (!TomlDocument.Same(TomlDocument.Plain(key.Property.GetValue(this)), value))
                    key.Property.SetValue(this, converted);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void Leave(Key key, string reason) => Warn($"{_path}: {key.Name} {reason}, so it was left unchanged");

    private void Warn(string message) => _log.Warn("io", "TomlFile: " + message);

    private static bool TryConvert(object value, Type type, [NotNullWhen(true)] out object? converted)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        converted = null;
        if (target.IsInstanceOfType(value))
            converted = value;
        else if (target == typeof(bool))
            converted = value switch
            {
                long number => number != 0,
                double number => number != 0 && !double.IsNaN(number),
                _ => null,
            };
        else if (target == typeof(int) && value is long and >= int.MinValue and <= int.MaxValue)
            converted = (int)(long)value;
        else if (target == typeof(double) && value is long whole)
            converted = (double)whole;
        else if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) && value is IReadOnlyList<object?> items)
        {
            var element = target.GetGenericArguments()[0];
            var list = Array.CreateInstance(element, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                if (!TryConvert(items[i]!, element, out var item))
                    return false;
                list.SetValue(item, i);
            }
            converted = list;
        }
        return converted is not null;
    }

    /// <summary>The text, or null for no file, or why it could not be read.</summary>
    private (string? Text, bool Bom, string Problem) ReadFile()
    {
        try
        {
            // Share the file so a save can replace it while this read is open.
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // The length is known before the bytes are. A file past the limit is not copied into a string.
            if (DataLimits.FileBytes(stream.Length, MaxFileBytes) is { } why)
                return (null, false, why);
            Span<byte> start = stackalloc byte[3];
            var bom = stream.Read(start) == 3 && start[0] == 0xEF && start[1] == 0xBB && start[2] == 0xBF;
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return (reader.ReadToEnd(), bom, "");
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, false, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, false, e.Message);
        }
    }

    /// <summary>Stops following the file. A write that was on its way runs now.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watch.Dispose();
        if (_writePending)
            Write();
        GC.SuppressFinalize(this);
    }

    private sealed class Key(PropertyInfo property)
    {
        public PropertyInfo Property { get; } = property;

        public string Name { get; } = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

        public string? Comment { get; } = property.GetCustomAttribute<TomlCommentAttribute>()?.Text;
    }
}
