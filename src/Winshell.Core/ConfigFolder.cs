namespace Winshell.Core;

/// <summary>
/// Where the shell reads its own files: <c>%USERPROFILE%\.config\winshell</c>, unless this process was pointed somewhere else.
/// The bar, which plugins it loads, the extensions it loads, and the taskbar setting live in <c>settings.json</c> here.
/// </summary>
public static class ConfigFolder
{
    /// <summary>The settings file's name.</summary>
    public const string SettingsFile = "settings.json";

    /// <summary>The environment variable that names an alternate folder. <see cref="ConfigSwitch"/> wins when both are set.</summary>
    public const string EnvironmentVariable = "WINSHELL_CONFIG";

    /// <summary>The command-line switch that names an alternate folder: <c>--config &lt;folder&gt;</c>.</summary>
    public const string ConfigSwitch = "--config";

    // Asked once. Reading the profile folder is a shell call, and the answer does not change during a run.
    private static readonly string Person =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "winshell");

    private static string? _chosen;

    /// <summary>The person's folder, whether or not <see cref="Use"/> has pointed this process elsewhere.</summary>
    public static string DefaultPath => Person;

    /// <summary>The folder this process reads. The person's folder, until <see cref="Use"/> points it elsewhere.</summary>
    public static string Path => _chosen ?? Person;

    /// <summary>
    /// Points this process at <paramref name="folder"/>. An empty value restores the person's folder.
    /// Call it before anything reads <see cref="Path"/>. A later call replaces the earlier one.
    /// </summary>
    /// <param name="folder">A full or relative path. Null or blank restores the person's folder.</param>
    public static void Use(string? folder) =>
        _chosen = string.IsNullOrWhiteSpace(folder) ? null : System.IO.Path.GetFullPath(folder);

    /// <summary>
    /// The folder this start should use. <c>--config</c> wins, then <see cref="EnvironmentVariable"/>, then the person's folder.
    /// This does not call <see cref="Use"/>. The first <c>--config</c> is the one that counts.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>A full path.</returns>
    /// <exception cref="ArgumentException"><c>--config</c> was given without a folder.</exception>
    public static string Resolve(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, ConfigSwitch, StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--config needs a folder.");
                }

                return System.IO.Path.GetFullPath(args[i + 1]);
            }

            const string equals = ConfigSwitch + "=";
            if (arg.StartsWith(equals, StringComparison.Ordinal))
            {
                var value = arg[equals.Length..];
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("--config needs a folder.");
                }

                return System.IO.Path.GetFullPath(value);
            }
        }

        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnv) ? Person : System.IO.Path.GetFullPath(fromEnv);
    }
}
