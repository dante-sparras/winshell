namespace Winshell.Apps;

/// <summary>A shortcut in the Recent folder: its name without <c>.lnk</c>, and its path.</summary>
/// <param name="Name">The shortcut's file name without <c>.lnk</c>, usually the document's name.</param>
/// <param name="Path">The full path of the shortcut, which opens the document.</param>
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

    /// <summary>Reads the shortcuts in <paramref name="folder"/>, the user's Recent folder.</summary>
    internal RecentDocuments(string folder)
    {
        _folder = folder;
    }

    /// <summary>
    /// Shortcuts whose name contains every word of <paramref name="query"/>, newest first, at most 20.
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
