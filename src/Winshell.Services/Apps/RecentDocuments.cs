namespace Winshell.Apps;

/// <summary>A shortcut in the Recent folder: its name without <c>.lnk</c>, and its path.</summary>
public sealed record RecentDocument(string Name, string Path);

/// <summary>
/// Recent documents Windows already keeps as shortcuts in the Recent folder.
/// This reads those names. It does not index files, and a blank query does not open the folder.
/// </summary>
public sealed class RecentDocuments
{
    /// <summary>How many matches <see cref="Search"/> returns.</summary>
    internal const int Limit = 20;

    private readonly string _folder;

    public RecentDocuments()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.Recent))
    {
    }

    /// <summary>Reads <paramref name="folder"/> instead of the real Recent folder. Tests pass a temporary one.</summary>
    internal RecentDocuments(string folder) => _folder = folder;

    /// <summary>
    /// Shortcuts whose name contains every word of <paramref name="query"/>, newest first, at most <see cref="Limit"/>.
    /// A blank query matches nothing and does not read a folder. The path is not searched.
    /// </summary>
    public IReadOnlyList<RecentDocument> Search(string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || _folder.Length == 0 || !Directory.Exists(_folder))
            return [];
        var found = new List<RecentFile>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_folder, "*.lnk"))
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
        return found.Take(Limit).Select(static file => new RecentDocument(file.Name, file.Path)).ToList();
    }

    private readonly record struct RecentFile(string Name, string Path, DateTime When);
}
