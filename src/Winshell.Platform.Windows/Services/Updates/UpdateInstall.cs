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
            foreach (var file in Files(staged))
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
    /// Hashes the saved zip again, extracts that zip, and copies the extract. A staged folder whose bytes were
    /// swapped after the download is not what gets copied. Returns null when it is in place, or why not.
    /// </summary>
    public static string? ApplyVerified(string updatesDir, string staged, string installDir) =>
        ApplyVerified(updatesDir, staged, installDir, Environment.IsPrivilegedProcess, UpdateTrust.CurrentRid());

    /// <summary>The same install, with the elevation decision supplied so a test can cover it without an elevated process.</summary>
    internal static string? ApplyVerified(string updatesDir, string staged, string installDir, bool privileged, int processRid)
    {
        var version = Path.GetFileName(staged);
        var zip = UpdateTrust.ZipPath(updatesDir, version);
        var digestPath = UpdateTrust.DigestPath(updatesDir, version);
        if (!File.Exists(zip) || !File.Exists(digestPath))
        {
            DeleteStaged(updatesDir, staged, version);
            return "The staged update has no saved digest, so Winshell will not copy it.";
        }
        string actual;
        string digest;
        try
        {
            actual = UpdateTrust.Hash(zip);
            digest = File.ReadAllText(digestPath).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
        if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
        {
            DeleteStaged(updatesDir, staged, version);
            return $"The staged update's SHA-256 is {actual}, but the saved digest says {digest.ToLowerInvariant()}.";
        }
        if (!UpdateTrust.ElevationAllows(privileged, UpdateTrust.LabelRid(zip), processRid)
            || !UpdateTrust.ElevationAllows(privileged, UpdateTrust.LabelRid(staged), processRid))
            return "An elevated Winshell will not install an update stored at a lower integrity.";
        var fresh = Path.Combine(UpdateTrust.Root(updatesDir), "extract-" + Guid.NewGuid().ToString("N"));
        var scratch = zip + "." + Guid.NewGuid().ToString("N") + ".copy";
        try
        {
            File.Copy(zip, scratch);
            if (!string.Equals(UpdateTrust.Hash(scratch), digest, StringComparison.OrdinalIgnoreCase))
            {
                DeleteStaged(updatesDir, staged, version);
                return "The staged update changed while Winshell was checking it.";
            }
            Directory.CreateDirectory(fresh);
            System.IO.Compression.ZipFile.ExtractToDirectory(scratch, fresh);
            var error = Apply(fresh, installDir);
            if (error is not null)
                return error;
            DeleteStaged(updatesDir, staged, version);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return e.Message;
        }
        finally
        {
            TryDeleteFile(scratch);
            if (Directory.Exists(fresh))
                TryDeleteDirectory(fresh);
        }
    }

    private static void DeleteStaged(string updatesDir, string staged, string version)
    {
        TryDeleteDirectory(staged);
        UpdateTrust.Discard(updatesDir, version);
    }

    /// <summary>Every file under <paramref name="root"/>. A link fails the copy instead of being followed.</summary>
    private static List<string> Files(string root)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"'{Path.GetRelativePath(root, child)}' is a link.");
                if ((File.GetAttributes(child) & FileAttributes.Directory) != 0)
                    pending.Push(child);
                else
                    files.Add(child);
            }
        }
        return files;
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
        UpdateTrust.Clean(updatesDir, current);
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
