using System.Text.Json;

namespace Winshell.Core;

/// <summary>The <c>extensions</c> list in the shell's settings: DLL paths the shell loads and starts.</summary>
public static class ExtensionList
{
    /// <summary>The key in <c>settings.json</c>.</summary>
    public const string Key = "extensions";

    /// <summary>
    /// Reads the paths from a settings object. A missing key, or a null, is an empty list.
    /// A value that is not a list, a blank entry, and an entry that is not text are skipped and noted.
    /// </summary>
    /// <param name="root">The merged settings object.</param>
    /// <param name="notes">Notes to append, or null to drop them.</param>
    /// <returns>The paths, in the order they were written. Relative paths stay relative.</returns>
    public static IReadOnlyList<string> Read(JsonElement root, ICollection<string>? notes = null)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(Key, out var value))
        {
            return [];
        }

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            notes?.Add("extensions is not a list");
            return [];
        }

        var paths = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                notes?.Add("extensions entry is not a path");
                continue;
            }

            var path = item.GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                notes?.Add("extensions entry is empty");
                continue;
            }

            paths.Add(path);
        }

        return paths;
    }
}
