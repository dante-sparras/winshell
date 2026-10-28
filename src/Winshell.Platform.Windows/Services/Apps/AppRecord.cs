namespace Winshell.Platform.Windows.Services.Apps;

/// <summary>One app the Start menu lists: a shortcut or a packaged app.</summary>
internal sealed record AppRecord(
    string Id,
    string Name,
    string Description,
    string Icon,
    string Target,
    string Arguments,
    string CommandLine,
    string StartIn,
    string AppId,
    IReadOnlyList<string> Folders,
    bool Hidden,
    bool Packaged,
    string LaunchPath);

/// <summary>How <c>Apps.search</c> scores a query. A higher band always beats a lower one; usage stays inside a band.</summary>
internal static class AppRank
{
    public const int Exact = 1_000_000;
    public const int Prefix = 800_000;
    public const int Word = 600_000;
    public const int Acronym = 500_000;
    public const int Contains = 400_000;
    public const int Fuzzy = 200_000;
    public const int Description = 100_000;
    public const int Target = 100_000;
    public const int Folder = 80_000;

    /// <summary>Zero when the query matches nothing on the app. Usage is added on top and cannot cross a band.</summary>
    public static int Score(string query, AppRecord app, AppUsage usage, long nowUnixMilliseconds)
    {
        var text = query.Trim();
        if (text.Length == 0)
            return 0;
        var best = 0;
        if (app.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
            best = Exact;
        else if (app.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            best = Prefix + Closeness(app.Name.Length - text.Length);
        var words = Words(app.Name);
        foreach (var word in words)
        {
            if (word.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, Word + Closeness(word.Length - text.Length));
        }
        if (text.Length >= 2)
        {
            var initials = Initials(words);
            if (initials.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                best = Math.Max(best, Acronym + Closeness(initials.Length - text.Length));
        }
        var at = app.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
            best = Math.Max(best, Contains - Math.Min(at, 200) * 10);
        if (FuzzyMatch(text, app.Name, out var skipped))
            best = Math.Max(best, Fuzzy - Math.Min(skipped, 50) * 1_000);
        if (app.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
            best = Math.Max(best, Description);
        if (ContainsTarget(app, text))
            best = Math.Max(best, Target);
        if (app.Folders.Any(folder => folder.Contains(text, StringComparison.OrdinalIgnoreCase)))
            best = Math.Max(best, Folder);
        return best == 0 ? 0 : best + UsageBoost(usage, nowUnixMilliseconds);
    }

    private static int Closeness(int extra) => Math.Max(0, 500 - extra);

    private static int UsageBoost(AppUsage usage, long nowUnixMilliseconds)
    {
        var count = Math.Clamp(usage.RunCount, 0, 100) * 100;
        if (usage.LastUsedUnixMilliseconds <= 0)
            return count;
        var age = nowUnixMilliseconds - usage.LastUsedUnixMilliseconds;
        if (age < 0)
            age = 0;
        var recency = age <= Hour ? 8_000 : age <= Day ? 5_000 : age <= 7 * Day ? 2_000 : age <= 30 * Day ? 500 : 0;
        return count + recency;
    }

    private const long Hour = 3_600_000;
    private const long Day = 86_400_000;

    private static bool ContainsTarget(AppRecord app, string query)
    {
        if (app.AppId.Contains(query, StringComparison.OrdinalIgnoreCase) || app.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (app.Target.Length == 0)
            return false;
        var file = Path.GetFileNameWithoutExtension(app.Target);
        return file.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var letter = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (letter && start < 0)
                start = i;
            if (!letter && start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }
        return words;
    }

    private static string Initials(List<string> words)
    {
        var letters = new List<char>(words.Count);
        foreach (var word in words)
        {
            if (word.Length > 0 && char.IsLetter(word[0]))
                letters.Add(word[0]);
        }
        return new string(letters.ToArray());
    }

    /// <summary>
    /// The query is a subsequence that starts on a word and skips at most as many letters as the query has.
    /// That catches "notpad" for Notepad and rejects a sparse match.
    /// </summary>
    private static bool FuzzyMatch(string query, string name, out int skipped)
    {
        skipped = 0;
        var index = 0;
        var gap = 0;
        var started = false;
        var atWord = true;
        foreach (var ch in name)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                atWord = true;
                continue;
            }
            var matches = index < query.Length && char.ToUpperInvariant(ch) == char.ToUpperInvariant(query[index]);
            if (matches && (started || atWord))
            {
                if (started)
                    skipped += gap;
                started = true;
                gap = 0;
                index++;
            }
            else if (started)
            {
                gap++;
            }
            atWord = false;
        }
        return index == query.Length && skipped <= query.Length;
    }
}

/// <summary>Builds the Start menu index and answers find, search, recent, and frequent.</summary>
internal static class AppIndex
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static string CommandLine(string target, string arguments)
    {
        if (target.Length == 0)
            return arguments;
        var quoted = target.Contains(' ') || target.Contains('\t') ? "\"" + target + "\"" : target;
        return arguments.Length == 0 ? quoted : quoted + " " + arguments;
    }

    public static List<AppRecord> Shortcuts(IEnumerable<string> folders)
    {
        var byId = new Dictionary<string, AppRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                continue;
            foreach (var path in Files(folder))
            {
                if (InStartup(folder, path))
                    continue;
                if (FromShortcut(folder, path) is { } entry)
                    byId[entry.Id] = entry;
            }
        }
        return byId.Values.ToList();
    }

    /// <summary>A packaged app, or null when Windows did not give it an id and a name.</summary>
    public static AppRecord? Packaged(string? appUserModelId, string? name, string? description)
    {
        var id = (appUserModelId ?? "").Trim();
        var display = (name ?? "").Trim();
        if (id.Length == 0 || display.Length == 0)
            return null;
        if (display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || display.StartsWith("@{", StringComparison.Ordinal))
            return null;
        var launch = AppsFolderPrefix + id;
        var text = (description ?? "").Trim();
        return new AppRecord(id, display, text, launch, launch, "", launch, "", id, [], false, true, id);
    }

    /// <summary>
    /// Shortcuts first, then packaged apps. A shortcut whose app id is a packaged app is dropped, so the app is
    /// listed once and launches by its AppUserModelID. The user's folder is later in <paramref name="shortcuts"/>,
    /// so it already replaced the common shortcut of the same id.
    /// </summary>
    public static List<AppRecord> Combine(IEnumerable<AppRecord> shortcuts, IEnumerable<AppRecord> packaged)
    {
        var packages = packaged.Where(app => app.Id.Length > 0).ToList();
        var packageIds = packages.Select(app => app.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, AppRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var shortcut in shortcuts)
        {
            if (shortcut.AppId.Length > 0 && packageIds.Contains(shortcut.AppId))
                continue;
            byId[shortcut.Id] = shortcut;
        }
        foreach (var package in packages)
            byId.TryAdd(package.Id, package);
        return byId.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static AppRecord? Find(IReadOnlyList<AppRecord> apps, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return apps.FirstOrDefault(app => app.Id == name)
            ?? apps.FirstOrDefault(app => app.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(app => app.AppId.Length > 0 && app.AppId == name)
            ?? apps.FirstOrDefault(app => app.AppId.Length > 0 && app.AppId.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(app => app.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static List<AppRecord> Search(IReadOnlyList<AppRecord> apps, string query, Func<AppRecord, AppUsage> usage, long nowUnixMilliseconds)
    {
        var ranked = new List<(AppRecord App, int Score, AppUsage Usage)>();
        foreach (var app in apps)
        {
            if (app.Hidden)
                continue;
            var use = usage(app);
            var score = AppRank.Score(query, app, use, nowUnixMilliseconds);
            if (score > 0)
                ranked.Add((app, score, use));
        }
        ranked.Sort(static (a, b) =>
        {
            var score = b.Score.CompareTo(a.Score);
            if (score != 0)
                return score;
            var count = b.Usage.RunCount.CompareTo(a.Usage.RunCount);
            if (count != 0)
                return count;
            var last = b.Usage.LastUsedUnixMilliseconds.CompareTo(a.Usage.LastUsedUnixMilliseconds);
            if (last != 0)
                return last;
            return StringComparer.CurrentCultureIgnoreCase.Compare(a.App.Name, b.App.Name);
        });
        return ranked.Select(row => row.App).ToList();
    }

    public static List<AppRecord> Recent(IReadOnlyList<AppRecord> apps, Func<AppRecord, AppUsage> usage) =>
        apps.Where(app => !app.Hidden && usage(app).LastUsedUnixMilliseconds > 0)
            .OrderByDescending(app => usage(app).LastUsedUnixMilliseconds)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static List<AppRecord> Frequent(IReadOnlyList<AppRecord> apps, Func<AppRecord, AppUsage> usage) =>
        apps.Where(app => !app.Hidden && usage(app).RunCount > 0)
            .OrderByDescending(app => usage(app).RunCount)
            .ThenByDescending(app => usage(app).LastUsedUnixMilliseconds)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static bool Same(AppRecord a, AppRecord b) =>
        a with { Folders = b.Folders } == b && a.Folders.SequenceEqual(b.Folders);

    private static IEnumerable<string> Files(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool InStartup(string folder, string path)
    {
        var directory = Path.GetDirectoryName(path) ?? folder;
        var relative = Path.GetRelativePath(folder, directory);
        if (relative == ".")
            return false;
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("Startup", StringComparison.OrdinalIgnoreCase));
    }

    private static AppRecord? FromShortcut(string folder, string path)
    {
        if (ShellLink.Read(path) is not { } link)
            return null;
        var name = Path.GetFileNameWithoutExtension(path);
        var relative = Path.ChangeExtension(Path.GetRelativePath(folder, path), null) ?? name;
        var id = relative.Replace('\\', '/');
        var directory = Path.GetDirectoryName(path) ?? folder;
        var relativeDirectory = Path.GetRelativePath(folder, directory);
        IReadOnlyList<string> folders = relativeDirectory == "."
            ? []
            : relativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var hasTarget = link.Target.Length > 0 && File.Exists(link.Target);
        var extension = hasTarget ? Path.GetExtension(link.Target).ToLowerInvariant() : "";
        var hidden = name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || extension is ".txt" or ".chm" or ".pdf" or ".htm" or ".html" or ".url" or ".rtf" or ".doc" or ".docx";
        var appId = link.AppUserModelId.Length > 0
            ? link.AppUserModelId
            : hasTarget && extension == ".exe" ? Path.GetFileNameWithoutExtension(link.Target).ToLowerInvariant() : "";
        var startIn = link.WorkingDirectory.Length > 0
            ? link.WorkingDirectory
            : hasTarget ? Path.GetDirectoryName(link.Target) ?? "" : "";
        var command = hasTarget ? CommandLine(link.Target, link.Arguments) : CommandLine("explorer.exe", "\"" + path + "\"");
        return new AppRecord(
            id,
            name,
            link.Description,
            path,
            hasTarget ? link.Target : "",
            link.Arguments,
            command,
            startIn,
            appId,
            folders,
            hidden,
            false,
            path);
    }
}
