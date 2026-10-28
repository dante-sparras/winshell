using Winshell.Runtime.Shell;

namespace Winshell;

/// <summary>Which folder a configuration is loaded from, and the one-time copy out of <c>~/.config/winshell</c>.</summary>
internal static class ConfigPaths
{
    /// <summary>When set, this folder is the config root and the old <c>~/.config/winshell</c> is not copied.</summary>
    public const string OverrideVariable = "WINSHELL_CONFIG_DIR";

    /// <summary><c>%APPDATA%\winshell</c>, or <see cref="OverrideVariable"/> when it is set.</summary>
    public static string Root() => RootFrom(Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>The config root for an override value. A blank value uses <see cref="ShellLocations.ConfigRoot"/>.</summary>
    internal static string RootFrom(string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
            return Path.GetFullPath(overrideValue.Trim());
        return ShellLocations.ConfigRoot;
    }

    /// <summary>The previous default: <c>%USERPROFILE%\.config\winshell</c>.</summary>
    public static string LegacyRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "winshell");

    /// <summary>Written under local state after the old folder has been considered, so the copy runs once.</summary>
    public static string MarkerPath() => Path.Combine(ShellLocations.StateRoot, "legacy-config-copied");

    /// <summary>
    /// Copies <see cref="LegacyRoot"/> into <see cref="ShellLocations.ConfigRoot"/> once.
    /// Files already in the new folder are left alone. The old folder is never deleted.
    /// Returns a message when files were copied.
    /// </summary>
    public static string? MigrateLegacy() => Migrate(LegacyRoot(), ShellLocations.ConfigRoot, MarkerPath());

    /// <summary>The copy <see cref="MigrateLegacy"/> performs, with the folders passed in so tests never touch the user profile.</summary>
    internal static string? Migrate(string legacy, string destination, string marker)
    {
        if (File.Exists(marker))
            return null;
        var sourceFull = Path.GetFullPath(legacy);
        var destFull = Path.GetFullPath(destination);
        string? message = null;
        // The destination sitting inside the source, or the other way around, would copy a tree into itself.
        if (!SamePath(sourceFull, destFull) && !Nested(sourceFull, destFull) && Directory.Exists(sourceFull))
        {
            var copied = CopyMissing(sourceFull, destFull);
            if (copied > 0)
            {
                var files = copied == 1 ? "1 file" : $"{copied} files";
                message = $"Copied {files} from \"{sourceFull}\" to \"{destFull}\". The old folder was left in place.";
            }
        }
        WriteMarker(marker, sourceFull);
        return message;
    }

    private static int CopyMissing(string source, string destination)
    {
        var info = new DirectoryInfo(source);
        // A junction or symlink can point at the destination, or at itself. Copy the files, not the link.
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return 0;
        var copied = 0;
        foreach (var file in info.EnumerateFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            Directory.CreateDirectory(destination);
            var target = Path.Combine(destination, file.Name);
            if (File.Exists(target))
                continue;
            file.CopyTo(target, overwrite: false);
            copied++;
        }
        foreach (var dir in info.EnumerateDirectories())
            copied += CopyMissing(dir.FullName, Path.Combine(destination, dir.Name));
        return copied;
    }

    private static void WriteMarker(string marker, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, source);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);

    private static bool Nested(string a, string b)
    {
        a = Trim(a) + Path.DirectorySeparatorChar;
        b = Trim(b) + Path.DirectorySeparatorChar;
        return b.StartsWith(a, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
