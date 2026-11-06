using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Winshell;

/// <summary>The comment written above a key the file does not have yet. A line break starts another comment line.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TomlCommentAttribute(string text) : Attribute
{
    /// <summary>The comment's text, without the leading <c>#</c>.</summary>
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
/// stays, as <see cref="TomlDocument"/> says. A key no property reads stays too, and when it differs from one only in
/// case, by a letter, or by two in a longer key, a warning names its line and the key it may be a misspelling of. A
/// write reads the file again and puts only assigned values into it, and the keys it does not have yet. When that read fails or does not parse, nothing is written and a warning is logged.
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
        _keys =
        [
            .. GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.MetadataToken)
                .Select(p => new Key(p))
        ];
        PropertyChanged += OnPropertyChanged;
        // Watching first, so a save between the two is not missed.
        _watch = new FileWatch(_path, loop, Follow);
        Follow();
    }

    /// <summary>Why the file could not be read, as <c>path[line:column]: message</c>, or empty when the last read parsed.</summary>
    public string Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
                ErrorChanged?.Invoke(_path, value);
        }
    }

    /// <summary>A TOML file's <see cref="Error"/> changed, on its loop: its path and the error, or empty when it is fixed.</summary>
    internal static event Action<string, string>? ErrorChanged;

    /// <summary>Whether the file is there and parses. Without it the properties keep the values they had.</summary>
    public bool Loaded
    {
        get => _loaded;
        private set => Set(ref _loaded, value);
    }

    /// <summary>Writes the file now, adding the keys it does not have, or making it and its folder when there is none.</summary>
    public void Save()
    {
        Write();
    }

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
        var load = TomlDocument.Load(_path);
        switch (load.Kind)
        {
            case TomlRead.Unreadable:
                // An editor can hold the file while it saves. That says nothing about the file.
                _log.Debug("io", $"TomlFile: failed to read {_path}: {load.Message}");
                return;
            case TomlRead.TooLarge:
                // A file past the limit is still the user's file.
                _text = null;
                Error = load.Describe(_path);
                Loaded = false;
                Warn($"failed to read {Error}");
                return;
            case TomlRead.Missing:
                _text = null;
                Error = "";
                Loaded = false;
                return;
            case TomlRead.Invalid:
                if (load.Text == _text)
                    return;
                _text = load.Text;
                Error = load.Describe(_path);
                Loaded = false;
                Warn($"failed to parse {Error}");
                return;
            case TomlRead.Parsed:
                if (load.Text == _text)
                    return;
                _text = load.Text;
                Error = "";
                Apply(load.Document!, false);
                _assigned.Clear();
                Loaded = true;
                return;
        }
    }

    private void Write()
    {
        _writePending = false;
        var load = TomlDocument.Load(_path);
        var document = load.Document;
        var error = load.Describe(_path);
        // No file yet is an empty document, so the assigned keys can create it.
        if (load.Kind == TomlRead.Missing)
            document = TomlDocument.Empty(_path);
        if (document is null)
        {
            Error = error;
            Loaded = false;
            Warn($"failed to parse {error}; the file was left unchanged");
            return;
        }

        Error = "";
        // A file that has not changed was applied already, and its refused values reported.
        if (load.Text != _text)
            Apply(document, true);
        var read = document.Values;
        foreach (var key in _keys)
        {
            var value = key.Property.GetValue(this);
            if (_assigned.Contains(key) || (value is not null && !read.ContainsKey(key.Name)))
                document.Set(key.Name, value, key.Comment);
        }

        var next = document.ToString();
        if (next != load.Text && !Put(next, load.Text, load.Bom))
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
            var written = AtomicFile.Save(_path, next, bom, read) == AtomicSave.Saved;
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
    /// refused, is reported and left: nothing writes a key that was not assigned, so the file keeps it as it is. So is a
    /// key no property reads that looks like a misspelling of one.
    /// </summary>
    private void Apply(TomlDocument document, bool keepAssigned)
    {
        string Where(string name)
        {
            return document.LocationOf(name);
        }

        // A key like none of these may be another reader's: a configuration can share preferences.toml.
        foreach (var name in document.Values.Keys)
            if (!_keys.Exists(key => key.Name == name) && Nearest(name) is { } near)
                Warn($"{Where(name)}: {name} was ignored; did you mean {near}?");
        _loading = true;
        try
        {
            foreach (var key in _keys)
            {
                if ((keepAssigned && _assigned.Contains(key)) || !document.Values.TryGetValue(key.Name, out var value))
                    continue;
                if (!TryConvert(value!, key.Property.PropertyType, out var converted))
                    Leave(Where(key.Name), key,
                        $"should be {Expected(key.Property.PropertyType)}, not {Shown(value!)}");
                else if (_check?.Invoke(key.Name, converted) is { Length: > 0 } reason)
                    Leave(Where(key.Name), key, reason);
                else if (!TomlDocument.Same(TomlDocument.Plain(key.Property.GetValue(this)), value))
                    key.Property.SetValue(this, converted);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The key <paramref name="name"/> may be a misspelling of: one that differs in case or in a letter or two.</summary>
    private string? Nearest(string name)
    {
        var best = (Name: (string?)null, Distance: 3);
        foreach (var key in _keys)
        {
            var distance = Distance(name.ToLowerInvariant(), key.Name.ToLowerInvariant());
            if (distance < best.Distance && (distance == 0 || distance < Math.Max(name.Length, key.Name.Length) / 2))
                best = (key.Name, distance);
        }

        return best.Name;
    }

    /// <summary>The fewest letters to add, remove or change to turn one word into the other.</summary>
    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1,
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }

        return previous[b.Length];
    }

    private void Leave(string where, Key key, string reason)
    {
        Warn($"{where}: {key.Name} {reason}, so it was left unchanged");
    }

    private void Warn(string message)
    {
        _log.Warn("io", "TomlFile: " + message);
    }

    /// <summary>What a property of <paramref name="type"/> takes, for a warning about a value it does not.</summary>
    private static string Expected(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return "a list of " + Plural(target.GetGenericArguments()[0]);
        if (target == typeof(bool))
            return "true or false";
        if (target == typeof(int))
            return string.Create(CultureInfo.InvariantCulture, $"a whole number from {int.MinValue} to {int.MaxValue}");
        if (target == typeof(long))
            return "a whole number";
        if (target == typeof(double))
            return "a number";
        return target == typeof(string) ? "a string" : "a table";
    }

    private static string Plural(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(bool))
            return "true or false values";
        if (target == typeof(int))
            return string.Create(CultureInfo.InvariantCulture, $"whole numbers from {int.MinValue} to {int.MaxValue}");
        if (target == typeof(long))
            return "whole numbers";
        if (target == typeof(double))
            return "numbers";
        if (target == typeof(string))
            return "strings";
        return target.IsGenericType && target.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? "lists"
            : "tables";
    }

    /// <summary>A value read from the file, as TOML, cut short past 100 characters.</summary>
    private static string Shown(object value)
    {
        var text = TomlDocument.Format(value) ?? "";
        if (text.Length <= 100)
            return text;
        return text[..(char.IsHighSurrogate(text[99]) ? 99 : 100)] + "…";
    }

    private static bool TryConvert(object value, Type type, [NotNullWhen(true)] out object? converted)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        converted = null;
        if (target.IsInstanceOfType(value))
        {
            converted = value;
        }
        else if (target == typeof(bool))
        {
            converted = value switch
            {
                long number => number != 0,
                double number => number != 0 && !double.IsNaN(number),
                _ => null
            };
        }
        else if (target == typeof(int) && value is long and >= int.MinValue and <= int.MaxValue)
        {
            converted = (int)(long)value;
        }
        else if (target == typeof(double) && value is long whole)
        {
            converted = (double)whole;
        }
        else if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) &&
                 value is IReadOnlyList<object?> items)
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
