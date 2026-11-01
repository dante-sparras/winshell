namespace Winshell;

/// <summary>The Windows folders Winshell uses for the current user.</summary>
public static class ShellLocations
{
    /// <summary>User configurations: <c>%APPDATA%\winshell</c>. These roam with the profile.</summary>
    public static string ConfigRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "winshell");

    /// <summary>
    /// Machine-local files: <c>%LOCALAPPDATA%\winshell</c>, or <c>WINSHELL_LOCAL_DIR</c> when it is set.
    /// Cache, logs, state, and per-shell data stay on this PC.
    /// </summary>
    public static string LocalRoot => LocalRootFrom(Environment.GetEnvironmentVariable("WINSHELL_LOCAL_DIR"));

    /// <summary>The local root for an override value. A blank value uses <c>%LOCALAPPDATA%\winshell</c>.</summary>
    internal static string LocalRootFrom(string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
            return Path.GetFullPath(overrideValue.Trim());
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winshell");
    }

    public static string DataRoot => Path.Combine(LocalRoot, "data");

    public static string StateRoot => Path.Combine(LocalRoot, "state");

    public static string CacheRoot => Path.Combine(LocalRoot, "cache");

    /// <summary>One folder per running instance. <c>log.txt</c> in that folder is the instance's log.</summary>
    public static string RunRoot => Path.Combine(LocalRoot, "run");

    /// <summary>Crash reports, one text file each. They stay on this PC.</summary>
    public static string CrashRoot => Path.Combine(LocalRoot, "crashes");

    /// <summary>The shell written out when the user has no configuration yet.</summary>
    public static string DefaultShellRoot => Path.Combine(LocalRoot, "default-shell");
}
