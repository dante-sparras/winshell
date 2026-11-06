using System.Globalization;
using Winshell.Native;

namespace Winshell.Updates;

/// <summary>An update put in place as Winshell started, or, with <see cref="Error"/>, one that was not.</summary>
internal sealed record InstalledUpdate(string Version, string? Error);

/// <summary>
/// Puts a staged update in place when Winshell starts. A build from source names no release repository, a packaged
/// Winshell is updated by its installer, and a winget install is updated by winget: none of those install a staged
/// update. A self-update would rewrite files winget has hashed.
/// </summary>
internal static class Updater
{
    /// <summary>
    /// Before a shell starts: deletes what the last update left behind and puts a staged update in place. Returns
    /// null when there was none to install, or when this copy does not install one, or the version and why it did
    /// not install, with <c>Error</c> null when it did.
    /// </summary>
    public static InstalledUpdate? InstallStaged(string? repository, string currentVersion)
    {
        // One gate: no repository, a package, or a winget copy.
        if (string.IsNullOrEmpty(repository) || Packaging.IsPackaged || IsWingetManaged(AppContext.BaseDirectory))
            return null;
        var current = ParseVersion(currentVersion) ?? new Version(0, 0, 0);
        var installDir = AppContext.BaseDirectory;
        UpdateInstall.CleanUp(installDir, UpdatesDir, current);
        if (UpdateInstall.Staged(UpdatesDir, current) is not { } staged)
            return null;
        // The update was saved by this user at medium integrity; an elevated process must not run what that user can rewrite.
        var error = Environment.IsPrivilegedProcess
            ? "An elevated Winshell does not install updates. Start Winshell normally to install it."
            : UpdateInstall.ApplyVerified(UpdatesDir, staged.Path, installDir);
        return new InstalledUpdate(staged.Version.ToString(3), error);
    }

    /// <summary>Staged updates, each unzipped in a folder named for its version.</summary>
    public static string UpdatesDir => Path.Combine(ShellLocations.LocalRoot, "updates");

    /// <summary>
    /// True when <paramref name="installDir"/> is winget's portable package folder or its Links alias.
    /// winget records a hash of that copy, so Winshell must not replace the files.
    /// </summary>
    internal static bool IsWingetManaged(string installDir)
    {
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet");
        return IsUnder(installDir, Path.Combine(winget, "Packages")) ||
               IsUnder(installDir, Path.Combine(winget, "Links"));
    }

    /// <summary><paramref name="path"/> is <paramref name="root"/> or a directory inside it.</summary>
    internal static bool IsUnder(string path, string root)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(baseDir, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads <c>v1.2.3</c> or <c>1.2.3</c>, ignoring a prerelease or build suffix.</summary>
    internal static Version? ParseVersion(string text)
    {
        var core = text.Trim().TrimStart('v', 'V');
        var end = core.IndexOfAny(['-', '+']);
        if (end >= 0)
            core = core[..end];
        var parts = core.Split('.');
        if (parts.Length != 3)
            return null;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        return new Version(numbers[0], numbers[1], numbers[2]);
    }
}

/// <summary>Whether this process runs with an MSIX package identity.</summary>
internal static class Packaging
{
    public static bool IsPackaged { get; } = PackageIdentity.Exists();
}
