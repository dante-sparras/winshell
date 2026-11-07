namespace Winshell;

/// <summary>
/// The folders Winshell uses for the current user. <see cref="Current"/> is the one every command and shell uses.
/// </summary>
/// <param name="ConfigRoot">User configurations: <c>%APPDATA%\winshell</c>. These roam with the profile.</param>
/// <param name="LocalRoot">Machine-local files: <c>%LOCALAPPDATA%\winshell</c>. Cache, logs, state and per-shell data stay on this PC.</param>
internal sealed record ShellLocations(string ConfigRoot, string LocalRoot)
{
    /// <summary>When set, this folder is <see cref="ConfigRoot"/>.</summary>
    public const string ConfigVariable = "WINSHELL_CONFIG_DIR";

    /// <summary>When set, this folder is <see cref="LocalRoot"/>. A child process inherits it.</summary>
    public const string LocalVariable = "WINSHELL_LOCAL_DIR";

    /// <summary>The folders under the Windows profile, or where <see cref="ConfigVariable"/> and <see cref="LocalVariable"/> say.</summary>
    public static ShellLocations Current => new(
        RootFrom(Environment.GetEnvironmentVariable(ConfigVariable), Environment.SpecialFolder.ApplicationData),
        RootFrom(Environment.GetEnvironmentVariable(LocalVariable), Environment.SpecialFolder.LocalApplicationData));

    public string DataRoot => Path.Combine(LocalRoot, "data");

    public string StateRoot => Path.Combine(LocalRoot, "state");

    public string CacheRoot => Path.Combine(LocalRoot, "cache");

    /// <summary>One folder per running instance. <c>log.txt</c> in that folder is the instance's log.</summary>
    public string RunRoot => Path.Combine(LocalRoot, "run");

    /// <summary>Crash reports, one text file each. They stay on this PC.</summary>
    public string CrashRoot => Path.Combine(LocalRoot, "crashes");

    public Instances Instances => new(RunRoot);

    /// <summary>A root for an override value: that folder, made full. A blank value is <c>winshell</c> in <paramref name="profile"/>.</summary>
    internal static string RootFrom(string? overrideValue, Environment.SpecialFolder profile)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
            return Path.GetFullPath(overrideValue.Trim());
        return Path.Combine(Environment.GetFolderPath(profile), "winshell");
    }
}
