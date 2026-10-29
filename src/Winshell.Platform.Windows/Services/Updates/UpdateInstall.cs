namespace Winshell.Platform.Windows.Services.Updates;

/// <summary>
/// Puts a downloaded update in place. Only a <c>winshell.exe</c> that has just started does this, before it runs a
/// shell, so no shell is using the files. A running file cannot be overwritten but can be moved, so each file the
/// update replaces moves into a <c>.winshell-old</c> folder next to it first; another Winshell still running keeps
/// running from there. A failure moves everything back. The next start deletes what it can of that folder.
/// </summary>
internal static class UpdateInstall
{
    /// <summary>Where replaced files wait to be deleted, in the install folder.</summary>
    public const string OldFolder = ".winshell-old";

    /// <summary>The newest unzipped update that is newer than <paramref name="current"/>, if there is one.</summary>
    public static (Version Version, string Path)? Staged(string updatesDir, Version current)
    {
        if (!Directory.Exists(updatesDir))
            return null;
        (Version Version, string Path)? best = null;
        foreach (var dir in Directory.EnumerateDirectories(updatesDir))
        {
            if (Updater.ParseVersion(Path.GetFileName(dir)) is not { } version || version <= current)
                continue;
            if (!File.Exists(Path.Combine(dir, "winshell.exe")))
                continue;
            if (best is null || version > best.Value.Version)
                best = (version, dir);
        }
        return best;
    }

    /// <summary>
    /// Copies the update in <paramref name="staged"/> over <paramref name="installDir"/>. Files the update does not
    /// have are left alone. Returns null when it is in place, or why not, with the install folder as it was.
    /// </summary>
    public static string? Apply(string staged, string installDir)
    {
        var old = Path.Combine(installDir, OldFolder, Guid.NewGuid().ToString("N"));
        var moved = new List<(string Target, string Old)>();
        var placed = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staged, file);
                var target = Path.Combine(installDir, relative);
                if (File.Exists(target))
                {
                    var aside = Path.Combine(old, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
                    File.Move(target, aside);
                    moved.Add((target, aside));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
                placed.Add(target);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var file in placed)
                TryDeleteFile(file);
            foreach (var (target, aside) in moved)
            {
                try { File.Move(aside, target, overwrite: true); }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException) { }
            }
            return e.Message;
        }
        TryDeleteDirectory(staged);
        return null;
    }

    /// <summary>
    /// Deletes the files an update replaced, and updates that are no newer than <paramref name="current"/> or never
    /// finished downloading. Whatever another running Winshell still holds stays until a later start.
    /// </summary>
    public static void CleanUp(string installDir, string updatesDir, Version current)
    {
        var old = Path.Combine(installDir, OldFolder);
        if (Directory.Exists(old))
        {
            foreach (var dir in Directory.EnumerateDirectories(old))
                TryDeleteDirectory(dir);
            TryDeleteDirectory(old);
        }
        if (!Directory.Exists(updatesDir))
            return;
        foreach (var dir in Directory.EnumerateDirectories(updatesDir))
        {
            if (Updater.ParseVersion(Path.GetFileName(dir)) is not { } version || version <= current)
                TryDeleteDirectory(dir);
        }
        foreach (var file in Directory.EnumerateFiles(updatesDir, "*.partial"))
            TryDeleteFile(file);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
