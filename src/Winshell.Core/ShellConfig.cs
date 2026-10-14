using System.Text.Json;

namespace Winshell.Core;

/// <summary>
/// The shell's own settings, read from the merged <c>settings.json</c>: the taskbar, the bar, and the extensions to load.
/// Anything else in the file belongs to an extension and is left unread here.
/// </summary>
public sealed class ShellConfig
{
    private ShellConfig(
        string taskbar,
        bool barVisible,
        BarEdge barEdge,
        BarLayoutDocument? bar,
        IReadOnlyList<string> extensions,
        IReadOnlyList<string> notes)
    {
        Taskbar = taskbar;
        BarVisible = barVisible;
        BarEdge = barEdge;
        Bar = bar;
        Extensions = extensions;
        Notes = notes;
    }

    /// <summary>The <c>taskbar</c> word. A missing key is <see cref="TaskbarSetting.Shown"/>.</summary>
    public string Taskbar { get; }

    /// <summary>True when <see cref="Taskbar"/> asks the shell to hide the Windows taskbar.</summary>
    public bool HidesTaskbar => TaskbarSetting.Hides(Taskbar);

    /// <summary>Whether the bar is drawn. A missing key leaves it drawn. Panels keep working when it is hidden.</summary>
    public bool BarVisible { get; }

    /// <summary>Which screen edge the bar occupies. A missing key is the top.</summary>
    public BarEdge BarEdge { get; }

    /// <summary>The <c>bar</c> object. Null when the key is missing, which means the installed default layout.</summary>
    public BarLayoutDocument? Bar { get; }

    /// <summary>DLL paths from <see cref="ExtensionList"/>, relative ones still relative.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Notes from the include merge and from keys this reader skipped. A missing file is a note.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// Reads <paramref name="path"/> and every file it includes.
    /// A missing file is the built-in default and a note. A file that is not JSON throws, so the caller can keep the previous one.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <returns>The shell's settings.</returns>
    /// <exception cref="JsonException">The root file is not JSON.</exception>
    public static ShellConfig Read(string path)
    {
        var read = ConfigJson.Read(path);
        var parsed = Parse(read.Json);
        if (read.Notes.Count == 0)
        {
            return parsed;
        }

        var notes = new List<string>(read.Notes.Count + parsed.Notes.Count);
        notes.AddRange(read.Notes);
        notes.AddRange(parsed.Notes);
        return new ShellConfig(parsed.Taskbar, parsed.BarVisible, parsed.BarEdge, parsed.Bar, parsed.Extensions, notes);
    }

    /// <summary>Reads the shell's keys from a merged settings object.</summary>
    /// <param name="json">The merged object, as <see cref="ConfigJson.Read"/> returns it.</param>
    /// <returns>The shell's settings.</returns>
    /// <exception cref="JsonException"><paramref name="json"/> is not JSON.</exception>
    public static ShellConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var notes = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            notes.Add("settings are not an object");
            return new ShellConfig(TaskbarSetting.Shown, barVisible: true, BarEdge.Top, bar: null, [], notes);
        }

        return new ShellConfig(ReadTaskbar(root, notes), ReadVisible(root, notes), ReadEdge(root, notes), ReadBar(root, notes), ExtensionList.Read(root, notes), notes);
    }

    private static string ReadTaskbar(JsonElement root, List<string> notes)
    {
        if (!root.TryGetProperty("taskbar", out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return TaskbarSetting.Shown;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? TaskbarSetting.Shown;
        }

        notes.Add("taskbar is not a word");
        return TaskbarSetting.Shown;
    }

    private static bool ReadVisible(JsonElement root, List<string> notes)
    {
        if (!root.TryGetProperty("barVisible", out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        notes.Add("barVisible is not true or false");
        return true;
    }

    private static BarEdge ReadEdge(JsonElement root, List<string> notes)
    {
        if (!root.TryGetProperty("barEdge", out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return BarEdge.Top;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            var edge = (BarEdge)number;
            if (Enum.IsDefined(edge))
            {
                return edge;
            }
        }
        else if (value.ValueKind == JsonValueKind.String && Enum.TryParse(value.GetString(), ignoreCase: true, out BarEdge named) && Enum.IsDefined(named))
        {
            return named;
        }

        notes.Add("barEdge is not an edge");
        return BarEdge.Top;
    }

    private static BarLayoutDocument? ReadBar(JsonElement root, List<string> notes)
    {
        if (!root.TryGetProperty("bar", out var value) || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            notes.Add("bar is not an object");
            return null;
        }

        return JsonSerializer.Deserialize(value, ShellLooseJson.Default.BarLayoutDocument);
    }
}
