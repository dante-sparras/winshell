using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Apps;

/// <summary>
/// Recent documents Windows already keeps as shortcuts in the Recent folder.
/// This reads those names. It does not index files, and a blank query does not open the folder.
/// </summary>
public sealed class RecentDocuments : QObject
{
    /// <summary>How many matches <see cref="Search"/> returns.</summary>
    internal const int Limit = 20;

    private readonly string? _folder;

    public RecentDocuments()
    {
    }

    /// <summary>Reads <paramref name="folder"/> instead of the real Recent folder. Tests pass a temporary one.</summary>
    internal RecentDocuments(string folder) => _folder = folder;

    /// <summary>
    /// Shortcuts whose name contains every word of <paramref name="query"/>, newest first, at most <see cref="Limit"/>.
    /// A blank query matches nothing and does not read a folder. The path is not searched.
    /// </summary>
    [Qml]
    public IReadOnlyList<Dictionary<string, object?>> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        return Matching(_folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Recent), query);
    }

    internal static List<Dictionary<string, object?>> Matching(string? folder, string? query)
    {
        var words = Words(query);
        if (words.Count == 0 || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return [];
        var found = new List<RecentFile>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                var name = Path.GetFileNameWithoutExtension(info.Name);
                if (name.Length == 0 || !words.All(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                    continue;
                found.Add(new RecentFile(name, info.FullName, info.LastWriteTimeUtc));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        found.Sort(static (a, b) =>
        {
            var byTime = b.When.CompareTo(a.When);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Name, b.Name);
        });
        if (found.Count > Limit)
            found.RemoveRange(Limit, found.Count - Limit);
        return found.Select(static file => new Dictionary<string, object?>
        {
            ["name"] = file.Name,
            ["path"] = file.Path,
        }).ToList();
    }

    private static List<string> Words(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private readonly record struct RecentFile(string Name, string Path, DateTime When);
}
