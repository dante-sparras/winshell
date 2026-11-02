using System.Security.Cryptography;
using System.Text;

namespace Winshell;

/// <summary>The id that names a shell across runs, and the folders named after it.</summary>
internal sealed record ShellIdentity(string ShellId, string DataDir, string StateDir, string CacheDir)
{
    /// <summary>The identity of a shell defined by <paramref name="rootFile"/>, its <c>shell.qml</c> or its program.</summary>
    /// <param name="rootFile">A full path. A relative folder is next to this file.</param>
    /// <param name="configRoot">The folder configurations live in, or empty when there is none.</param>
    /// <param name="shellId">The id it asks for. Null names it after where it is.</param>
    /// <param name="dataDir">Another data folder. Null is the one named after the id. <paramref name="stateDir"/> and <paramref name="cacheDir"/> are the same.</param>
    /// <param name="stateDir">Another state folder.</param>
    /// <param name="cacheDir">Another cache folder.</param>
    public static ShellIdentity Resolve(string rootFile, string configRoot, string? shellId, string? dataDir, string? stateDir, string? cacheDir)
    {
        var id = shellId ?? DefaultId(rootFile, configRoot);
        var shellDir = Path.GetDirectoryName(rootFile)!;
        return new ShellIdentity(
            id,
            Expand(dataDir, ShellLocations.DataRoot, shellDir) ?? Path.Combine(ShellLocations.DataRoot, "by-shell", id),
            Expand(stateDir, ShellLocations.StateRoot, shellDir) ?? Path.Combine(ShellLocations.StateRoot, "by-shell", id),
            Expand(cacheDir, ShellLocations.CacheRoot, shellDir) ?? Path.Combine(ShellLocations.CacheRoot, "by-shell", id));
    }

    /// <summary>
    /// A config in the config folder is named after its folder: <c>root</c> for the folder itself, or the <c>-c</c>
    /// name. Moving the config folder, or switching between <c>-p</c> and <c>-c</c>, keeps its data and state. Any
    /// other shell has only its path, hashed.
    /// </summary>
    public static string DefaultId(string rootFile, string configRoot)
    {
        var dir = Path.GetDirectoryName(rootFile)!;
        if (configRoot.Length > 0)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configRoot));
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                return "root";
            if (string.Equals(Path.GetDirectoryName(dir), root, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(dir).ToLowerInvariant();
        }
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(rootFile.ToLowerInvariant())));
    }

    /// <summary>
    /// A given folder is relative to the shell file, and <c>%NAME%</c> is expanded.
    /// <c>$BASE</c> is the folder Winshell would have used for that kind of data.
    /// </summary>
    private static string? Expand(string? folder, string baseDir, string shellDir)
    {
        if (folder is null)
            return null;
        var text = Environment.ExpandEnvironmentVariables(folder.Replace("$BASE", baseDir, StringComparison.Ordinal));
        // An empty pragma used to fail in GetFullPath. Keep that, instead of treating it as the shell folder.
        if (text.Length == 0)
            return Path.GetFullPath(text);
        if (!Path.IsPathRooted(text))
            text = Path.Combine(shellDir, text);
        return Path.GetFullPath(text);
    }
}
