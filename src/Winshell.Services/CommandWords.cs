namespace Winshell;

/// <summary>
/// The words of Winshell's command line that more than one reader or writer uses: the CLI, a configuration's program
/// and the sign-in command. Each is spelled here once.
/// </summary>
internal static class CommandWords
{
    public const string Run = "run";

    /// <summary>Ends Winshell's words. It and every word after it belong to the configuration's program.</summary>
    public const string End = "--";

    public static readonly CommandWord Path = new("--path", "-p");

    public static readonly CommandWord Config = new("--config", "-c");

    public static readonly CommandWord Daemonize = new("--daemonize", "-d");

    public static readonly CommandWord NoDuplicate = new("--no-duplicate", "-n");

    public static readonly CommandWord NoBuild = new("--no-build");

    public static readonly CommandWord Verbose = new("--verbose", "-v");

    /// <summary>The process a reload's program waits for. A configuration's program reads it. The CLI does not take it.</summary>
    public static readonly CommandWord After = new("--after");

    /// <summary>Whether <paramref name="name"/> is one folder of the config folder, so it can't leave it. A path goes in -p.</summary>
    public static bool IsConfigName(string name)
    {
        return name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(['/', '\\', ':']) < 0;
    }
}

/// <summary>An option's long spelling and the shorter ones it also answers to.</summary>
internal sealed record CommandWord(string Long, params string[] Shorts)
{
    public string Short => Shorts[0];

    public bool Is(string token)
    {
        return token == Long || Array.IndexOf(Shorts, token) >= 0;
    }
}
