using System.Text;
using System.Text.Json;

namespace Winshell.Core;

/// <summary>The <c>manifest.json</c> that identifies a plugin.</summary>
public sealed class PluginManifest
{
    /// <summary>
    /// The largest manifest that is read, in bytes. A shipped one is under two kilobytes.
    /// The bar thread reads every manifest at startup and on a reload, and a sheet's again on each save.
    /// </summary>
    internal const int MaxBytes = 1 << 20;

    private PluginManifest(
        string id,
        string name,
        IReadOnlyList<PluginKind> kinds,
        bool firstParty,
        string assembly,
        string entry,
        string defaultSection,
        string path,
        IReadOnlyList<string> capabilities,
        string clonedFrom,
        bool keepLoaded,
        IReadOnlyList<PluginSetting> schema,
        IReadOnlyList<PluginCommandInfo> commands,
        IReadOnlyList<PluginChord> chords,
        PluginSheet? sheet)
    {
        Id = id;
        Name = name;
        Kinds = kinds;
        FirstParty = firstParty;
        Assembly = assembly;
        Entry = entry;
        DefaultSection = defaultSection;
        Path = path;
        Capabilities = capabilities;
        ClonedFrom = clonedFrom;
        KeepLoaded = keepLoaded;
        Schema = schema;
        Commands = commands;
        Chords = chords;
        Sheet = sheet;
    }

    /// <summary>Stable id, such as <c>winoma.clock</c>.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Kinds this plugin declares.</summary>
    public IReadOnlyList<PluginKind> Kinds { get; }

    /// <summary>True when the plugin shipped with Winoma.</summary>
    public bool FirstParty { get; }

    /// <summary>Assembly file name or path named by the manifest.</summary>
    public string Assembly { get; }

    /// <summary>Type that implements the plugin.</summary>
    public string Entry { get; }

    /// <summary><c>left</c>, <c>center</c>, or <c>right</c>.</summary>
    public string DefaultSection { get; }

    /// <summary>Full path of the manifest file.</summary>
    public string Path { get; }

    /// <summary>Capability names this plugin asks to bind. Empty for a built-in, which keeps the family of its id.</summary>
    public IReadOnlyList<string> Capabilities { get; }

    /// <summary>Built-in id this plugin stands in for. Empty when it does not.</summary>
    public string ClonedFrom { get; }

    /// <summary>True when a service should stay loaded across a plugin reload.</summary>
    public bool KeepLoaded { get; }

    /// <summary>Settings <c>winoma bar set</c> may write on this plugin's slot.</summary>
    public IReadOnlyList<PluginSetting> Schema { get; }

    /// <summary>Commands a chord or <c>winoma plugin run</c> may send. Each is <c>plugin:&lt;id&gt;:&lt;name&gt;</c> in <c>bindings.json</c>.</summary>
    public IReadOnlyList<PluginCommandInfo> Commands { get; }

    /// <summary>Chords a new <c>bindings.json</c> binds to <see cref="Commands"/>.</summary>
    public IReadOnlyList<PluginChord> Chords { get; }

    /// <summary>The face and panel, when this plugin is a sheet. Null for a plugin that has an assembly.</summary>
    public PluginSheet? Sheet { get; }

    /// <summary>True when the manifest is the widget and there is no assembly to build.</summary>
    public bool IsSheet => Sheet is not null;

    /// <summary>Reads a manifest. <paramref name="error"/> explains a rejected file.</summary>
    public static bool TryRead(string path, bool firstParty, out PluginManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;
        try
        {
            if (ReadText(path) is not string text)
            {
                error = "manifest " + path + " is too large";
                return false;
            }

            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "manifest is not an object";
                return false;
            }

            if (root.TryGetProperty("schemaVersion", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != 1))
            {
                error = "manifest schemaVersion must be 1";
                return false;
            }

            var id = String(root, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                error = "manifest is missing id";
                return false;
            }

            if (!root.TryGetProperty("kinds", out var kindsElement) || kindsElement.ValueKind != JsonValueKind.Array)
            {
                error = id + " is missing kinds";
                return false;
            }

            var kinds = new List<PluginKind>();
            foreach (var kind in kindsElement.EnumerateArray())
            {
                if (kind.ValueKind != JsonValueKind.String || !PluginKindNames.TryParse(kind.GetString(), out var parsed))
                {
                    error = id + " has an unknown kind";
                    return false;
                }

                kinds.Add(parsed);
            }

            if (kinds.Count == 0)
            {
                error = id + " has no kinds";
                return false;
            }

            var section = "center";
            IReadOnlyList<PluginSetting> fields = [];
            if (root.TryGetProperty("barWidget", out var widget) && widget.ValueKind == JsonValueKind.Object)
            {
                var declared = String(widget, "defaultSection");
                if (declared is "left" or "center" or "right")
                {
                    section = declared;
                }

                if (!TrySchema(widget, id, out fields, out error))
                {
                    return false;
                }
            }

            if (!TryList(root, "capabilities", id, out var capabilities, out error))
            {
                return false;
            }

            if (!TryCommands(root, id, out var commands, out error) || !TryChords(root, id, commands, out var chords, out error))
            {
                return false;
            }

            var display = String(root, "name") is { Length: > 0 } name ? name : id;
            var assembly = String(root, "assembly");
            var entry = String(root, "entry");
            if (!TrySheet(root, id, display, assembly, entry, out var sheet, out error))
            {
                return false;
            }

            manifest = new PluginManifest(
                id,
                display,
                kinds,
                firstParty,
                assembly,
                entry,
                section,
                path,
                capabilities,
                String(root, "clonedFrom"),
                Bool(root, "keepLoaded"),
                fields,
                commands,
                chords,
                sheet);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A string that holds half a surrogate pair, such as "\ud83d", parses and then throws InvalidOperationException when it is read.
            error = "could not read manifest " + path;
            return false;
        }
    }

    /// <summary>The text of the manifest at <paramref name="path"/>, read as <see cref="File.ReadAllText(string)"/> reads it.</summary>
    /// <param name="path">Full path of a <c>manifest.json</c>.</param>
    /// <returns>The text, or null when the file is larger than <see cref="MaxBytes"/> and was not read.</returns>
    internal static string? ReadText(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static bool TrySheet(JsonElement root, string id, string name, string assembly, string entry, out PluginSheet? sheet, out string? error)
    {
        sheet = null;
        error = null;
        if (!root.TryGetProperty("sheet", out var element))
        {
            return true;
        }

        if (assembly.Length > 0 || entry.Length > 0)
        {
            error = id + " has both an assembly and a sheet";
            return false;
        }

        return PluginSheet.TryRead(element, id, name, out sheet, out error);
    }

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool TryList(JsonElement element, string name, string id, out IReadOnlyList<string> values, out string? error)
    {
        values = [];
        error = null;
        if (!element.TryGetProperty(name, out var array))
        {
            return true;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            error = id + " " + name + " must be an array";
            return false;
        }

        var list = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                error = id + " " + name + " must be strings";
                return false;
            }

            list.Add(item.GetString() ?? "");
        }

        values = list;
        return true;
    }

    private static bool TrySchema(JsonElement widget, string id, out IReadOnlyList<PluginSetting> schema, out string? error)
    {
        schema = [];
        error = null;
        if (!widget.TryGetProperty("schema", out var array))
        {
            return true;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            error = id + " barWidget.schema must be an array";
            return false;
        }

        var list = new List<PluginSetting>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = id + " barWidget.schema must be objects";
                return false;
            }

            var key = String(item, "key");
            if (key.Length == 0)
            {
                error = id + " barWidget.schema is missing a key";
                return false;
            }

            if (!seen.Add(key))
            {
                error = id + " barWidget.schema repeats " + key;
                return false;
            }

            if (!item.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                error = id + " has an unknown setting type";
                return false;
            }

            var type = typeElement.GetString() ?? "";
            if (type is not ("string" or "number" or "bool"))
            {
                error = id + " has an unknown setting type " + type;
                return false;
            }

            var label = key;
            if (item.TryGetProperty("label", out var labelElement))
            {
                if (labelElement.ValueKind != JsonValueKind.String)
                {
                    error = id + " barWidget.schema label must be a string";
                    return false;
                }

                var text = labelElement.GetString() ?? "";
                if (text.Length > 0)
                {
                    label = text;
                }
            }

            list.Add(new PluginSetting(key, type, label));
        }

        schema = list;
        return true;
    }

    private static bool TryCommands(JsonElement root, string id, out IReadOnlyList<PluginCommandInfo> commands, out string? error)
    {
        commands = [];
        error = null;
        if (!root.TryGetProperty("commands", out var array))
        {
            return true;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            error = id + " commands must be an array";
            return false;
        }

        var list = new List<PluginCommandInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = id + " commands must be objects";
                return false;
            }

            var name = String(item, "name");
            if (!PluginCommandIds.IsName(name))
            {
                error = id + " has a command without a usable name";
                return false;
            }

            if (!seen.Add(name))
            {
                error = id + " commands repeats " + name;
                return false;
            }

            list.Add(new PluginCommandInfo(name, String(item, "summary"), Bool(item, "hold")));
        }

        commands = list;
        return true;
    }

    /// <summary>
    /// Rejects a chord a manifest wants to bind. The shell does not know which chords the window manager keeps,
    /// so the product that loads plugins installs this once. While it is null, no chord is accepted.
    /// </summary>
    /// <remarks>The function returns the rest of the error after the plugin id and a space, or null when the chord may be bound.</remarks>
    internal static Func<string, string?>? CheckChord { get; set; }

    private static bool TryChords(JsonElement root, string id, IReadOnlyList<PluginCommandInfo> commands, out IReadOnlyList<PluginChord> chords, out string? error)
    {
        chords = [];
        error = null;
        if (!root.TryGetProperty("chords", out var array))
        {
            return true;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            error = id + " chords must be an array";
            return false;
        }

        var list = new List<PluginChord>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = id + " chords must be objects";
                return false;
            }

            var chord = String(item, "chord");
            // No checker installed rejects the chord. The product installs one that knows the window manager's chord text.
            var problem = CheckChord is null ? "binds an unknown chord " + chord : CheckChord(chord);
            if (problem is not null)
            {
                error = id + " " + problem;
                return false;
            }

            var command = String(item, "command");
            if (!commands.Any(declared => declared.Name.Equals(command, StringComparison.OrdinalIgnoreCase)))
            {
                error = id + " binds " + chord + " to a command it does not declare";
                return false;
            }

            list.Add(new PluginChord(chord, command));
        }

        chords = list;
        return true;
    }
}

/// <summary>One value a bar slot may store for a plugin.</summary>
/// <param name="Key">Setting name.</param>
/// <param name="Type"><c>string</c>, <c>number</c>, or <c>bool</c>.</param>
/// <param name="Label">Text for a person. The key when the manifest omits it.</param>
public sealed record PluginSetting(string Key, string Type, string Label);
