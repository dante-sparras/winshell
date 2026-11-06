using System.Diagnostics;
using System.IO.Compression;
using Winshell.Native;

namespace Winshell.Updates;

/// <summary>
/// Puts a staged update in place. Only a <c>winshell.exe</c> that has just started does this, before it runs a
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
                try
                {
                    File.Move(aside, target, true);
                }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                {
                }

            return e.Message;
        }

        TryDeleteDirectory(staged);
        return null;
    }

    /// <summary>
    /// Hashes the saved zip again, extracts that zip, and copies the extract. A staged folder whose bytes were
    /// swapped after the download is not what gets copied. The extract's <c>winshell.exe</c> must carry the version the
    /// staged folder is named for, so a release's tag cannot cover an older build. When the installed
    /// <c>winshell.exe</c> is signed, every <c>.exe</c> and <c>.dll</c> in the update must be signed too.
    /// Returns null when it is in place, or why not.
    /// </summary>
    public static string? ApplyVerified(string updatesDir, string staged, string installDir)
    {
        return ApplyVerified(updatesDir, staged, installDir, Authenticode.Signer);
    }

    /// <summary>
    /// The same install, with <paramref name="signer"/> giving the subject that signed a file, or null when it is not
    /// validly signed, so a test can cover signed installs without certificates.
    /// </summary>
    internal static string? ApplyVerified(string updatesDir, string staged, string installDir,
        Func<string, string?> signer)
    {
        var version = Path.GetFileName(staged);
        var zip = UpdateTrust.ZipPath(updatesDir, version);
        var digestPath = UpdateTrust.DigestPath(updatesDir, version);
        if (!File.Exists(zip) || !File.Exists(digestPath))
        {
            DeleteStaged(updatesDir, staged, version);
            return "The staged update has no saved digest, so Winshell will not copy it.";
        }

        var fresh = UpdateTrust.ExtractPath(updatesDir);
        try
        {
            var digest = File.ReadAllText(digestPath).Trim();
            string? mismatch = null;
            // One handle hashes and extracts, and FileShare.Read keeps anyone from writing the zip in between.
            using (var stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var actual = UpdateTrust.Hash(stream);
                if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                {
                    mismatch =
                        $"The staged update's SHA-256 is {actual}, but the saved digest says {digest.ToLowerInvariant()}.";
                }
                else
                {
                    stream.Position = 0;
                    ZipFile.ExtractToDirectory(stream, fresh);
                }
            }

            mismatch ??= VersionMismatch(fresh, version) ?? SignerMismatch(fresh, installDir, signer);
            if (mismatch is not null)
            {
                DeleteStaged(updatesDir, staged, version);
                return mismatch;
            }

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
            if (Directory.Exists(fresh))
                TryDeleteDirectory(fresh);
        }
    }

    /// <summary>Why <paramref name="extract"/> is not the release <paramref name="version"/>, or null when its <c>winshell.exe</c> is.</summary>
    private static string? VersionMismatch(string extract, string version)
    {
        var product = FileVersionInfo.GetVersionInfo(Path.Combine(extract, "winshell.exe")).ProductVersion;
        if (Updater.ParseVersion(product ?? "")?.ToString(3) == version)
            return null;
        return
            $"The update is Winshell {version}, but its winshell.exe is {(string.IsNullOrEmpty(product) ? "unversioned" : product)}, so Winshell does not install it.";
    }

    /// <summary>
    /// Why the update in <paramref name="extract"/> must not replace a signed install, or null when it may. An
    /// unsigned install, such as a build before releases were signed, takes any update whose digest matched; a signed
    /// one takes only an update whose every <c>.exe</c> and <c>.dll</c> is signed by its own signer or, for the .NET
    /// runtime and the libraries, by Microsoft, and that has each of <see cref="UpdateSignature.Signed"/>.
    /// </summary>
    private static string? SignerMismatch(string extract, string installDir, Func<string, string?> signer)
    {
        if (signer(Path.Combine(installDir, "winshell.exe")) is not { } expected)
            return null;
        foreach (var name in UpdateSignature.Signed)
            if (!File.Exists(Path.Combine(extract, name)))
                return $"This Winshell is signed, but the update has no {name}, so Winshell does not install it.";
        foreach (var file in Files(extract))
        {
            if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            var actual = signer(file);
            if (actual != expected && !UpdateSignature.IsMicrosoft(actual))
            {
                var by = actual is null ? "is not validly signed" : $"is signed by {actual}";
                return
                    $"This Winshell is signed by {expected}, but the update's {Path.GetRelativePath(extract, file)} {by}, so Winshell does not install it.";
            }
        }

        return null;
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
            foreach (var child in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"'{Path.GetRelativePath(root, child)}' is a link.");
                if ((File.GetAttributes(child) & FileAttributes.Directory) != 0)
                    pending.Push(child);
                else
                    files.Add(child);
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

        UpdateTrust.Clean(updatesDir, current);
        if (!Directory.Exists(updatesDir))
            return;
        foreach (var dir in Directory.EnumerateDirectories(updatesDir))
            if (Updater.ParseVersion(Path.GetFileName(dir)) is not { } version || version <= current)
                TryDeleteDirectory(dir);
        foreach (var file in Directory.EnumerateFiles(updatesDir, "*.partial"))
            TryDeleteFile(file);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
