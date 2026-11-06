using System.Security.Cryptography;

namespace Winshell.Updates;

/// <summary>
/// The verified zip and its digest, kept beside the updates folder. The version folder can be rewritten after the
/// zip was saved; apply hashes this zip again and extracts that, so a swapped file in the version folder is not copied.
/// </summary>
internal static class UpdateTrust
{
    internal static string Root(string updatesDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(updatesDir));
        return Path.Combine(string.IsNullOrEmpty(parent) ? updatesDir : parent, "update-trust");
    }

    internal static string ZipPath(string updatesDir, string version)
    {
        return Path.Combine(Root(updatesDir), version + ".zip");
    }

    internal static string DigestPath(string updatesDir, string version)
    {
        return Path.Combine(Root(updatesDir), version + ".sha256");
    }

    /// <summary>A scratch folder for one apply to extract into. <see cref="Clean"/> deletes any a crash left.</summary>
    internal static string ExtractPath(string updatesDir)
    {
        return Path.Combine(Root(updatesDir), "extract-" + Guid.NewGuid().ToString("N"));
    }

    internal static void Discard(string updatesDir, string version)
    {
        TryDelete(ZipPath(updatesDir, version));
        TryDelete(DigestPath(updatesDir, version));
        TryDelete(DigestPath(updatesDir, version) + ".partial");
    }

    /// <summary>
    /// Deletes saved zips and digests for versions that are no newer than <paramref name="current"/>, and the scratch
    /// a crash leaves: <c>*.partial</c> files and <c>extract-*</c> folders from an apply.
    /// </summary>
    internal static void Clean(string updatesDir, Version current)
    {
        var root = Root(updatesDir);
        if (!Directory.Exists(root))
            return;
        foreach (var file in Directory.EnumerateFiles(root))
            if (file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
                || (Updater.ParseVersion(Path.GetFileNameWithoutExtension(file)) is { } version && version <= current))
                TryDelete(file);
        foreach (var dir in Directory.EnumerateDirectories(root, "extract-*"))
            TryDeleteDirectory(dir);
    }

    internal static string Hash(Stream stream)
    {
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void TryDelete(string path)
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
