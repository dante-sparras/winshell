using System.Security.Cryptography;
using System.Text;

namespace Winshell;

/// <summary>The id that names a shell across runs, and the folders named after it.</summary>
internal sealed record ShellIdentity(string ShellId, string DataDir, string StateDir, string CacheDir)
{
    /// <summary>
    /// The default folders an earlier Winshell named after a hash of the program, each with the folder it moves to.
    /// Empty when the id or the folder is given, or when the id is still that hash.
    /// </summary>
    private IReadOnlyList<(string From, string To)> Earlier { get; init; } = [];

    /// <summary>The identity of a shell defined by <paramref name="program"/>.</summary>
    /// <param name="program">A full path. A relative folder is next to it, and a program a reload built in <c>bin\shell.next</c> counts as in <c>bin\shell</c>.</param>
    /// <param name="locations">The folders: configurations in <see cref="ShellLocations.ConfigRoot"/>, which can be empty when there is none, and the named folders under its local root.</param>
    /// <param name="shellId">The id it asks for. Null names it after where it is.</param>
    /// <param name="dataDir">Another data folder. Null is the one named after the id. <paramref name="stateDir"/> and <paramref name="cacheDir"/> are the same.</param>
    /// <param name="stateDir">Another state folder.</param>
    /// <param name="cacheDir">Another cache folder.</param>
    public static ShellIdentity Resolve(string program, ShellLocations locations, string? shellId, string? dataDir,
        string? stateDir, string? cacheDir)
    {
        var id = shellId ?? DefaultId(program, locations.ConfigRoot);
        var earlier = shellId is null ? Hashed(program) : id;
        var shellDir = Path.GetDirectoryName(ConfigurationProject.InOutputFolder(program) ?? program)!;
        var moves = new List<(string, string)>();

        string Folder(string? given, string root)
        {
            if (Expand(given, root, shellDir) is { } folder)
                return folder;
            if (earlier != id)
                moves.Add((Path.Combine(root, "by-shell", earlier), Path.Combine(root, "by-shell", id)));
            return Path.Combine(root, "by-shell", id);
        }

        return new ShellIdentity(id, Folder(dataDir, locations.DataRoot), Folder(stateDir, locations.StateRoot),
            Folder(cacheDir, locations.CacheRoot)) { Earlier = moves };
    }

    /// <summary>
    /// A configuration in the config folder is named after its project folder: <c>root</c> for the folder itself, or the
    /// <c>-c</c> name. Moving the config folder, or switching between <c>-p</c> and <c>-c</c>, keeps its data and state.
    /// Any other shell has only its path, hashed.
    /// </summary>
    private static string DefaultId(string program, string configRoot)
    {
        if (configRoot.Length > 0 && ConfigurationProject.FolderOf(program) is { } folder)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configRoot));
            if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase))
                return "root";
            if (string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(folder).ToLowerInvariant();
        }

        return Hashed(program);
    }

    /// <summary>The program's path, hashed. A program a reload built beside the running one is the same program.</summary>
    private static string Hashed(string program)
    {
        var path = ConfigurationProject.InOutputFolder(program) ?? program;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..32];
    }

    /// <summary>
    /// Moves each folder an earlier Winshell kept under the hashed id to where this id keeps it, when that is not there
    /// yet, so a configuration in the config folder keeps its data, state and cache. Nothing is deleted. A folder that
    /// cannot move, because a file in it is open, stays where it is.
    /// </summary>
    public void CarryOver(Log log)
    {
        foreach (var (from, to) in Earlier)
        {
            if (!Directory.Exists(from) || Path.Exists(to))
                continue;
            try
            {
                Directory.Move(from, to);
                log.Info("shell", $"Moved \"{from}\" to \"{to}\"");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Warn("shell", $"Could not move \"{from}\" to \"{to}\", so it stays where it is: {e.Message}");
            }
        }
    }

    /// <summary>
    /// A given folder is relative to <paramref name="shellDir"/>, and <c>%NAME%</c> is expanded.
    /// <c>$BASE</c> is the folder Winshell would have used for that kind of data.
    /// </summary>
    private static string? Expand(string? folder, string baseDir, string shellDir)
    {
        if (folder is null)
            return null;
        var text = Environment.ExpandEnvironmentVariables(folder.Replace("$BASE", baseDir, StringComparison.Ordinal));
        // An empty folder fails in GetFullPath, and does not read as the shell folder.
        if (text.Length == 0)
            return Path.GetFullPath(text);
        if (!Path.IsPathRooted(text))
            text = Path.Combine(shellDir, text);
        return Path.GetFullPath(text);
    }
}
