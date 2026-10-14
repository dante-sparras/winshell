namespace Winshell.Core;

/// <summary>The word in the shell's <c>lifetime</c> file.</summary>
public enum ShellLife
{
    /// <summary>The shell is up. One that exits under this word crashed.</summary>
    Running,

    /// <summary>The shell was asked to stop, or the session is ending. It stays down.</summary>
    Stopped,

    /// <summary>The shell is ended so another build can take its place.</summary>
    Replacing,
}

/// <summary>What the shell's watcher does once the shell has exited.</summary>
public enum ShellExit
{
    /// <summary>Do nothing. A shell holds the process, and what the old one left is the new one's now.</summary>
    Leave,

    /// <summary>Ask again in a moment. The shell that takes this one's place may still be coming.</summary>
    Wait,

    /// <summary>Start the shell again.</summary>
    Restart,

    /// <summary>Give Windows its taskbar back and leave the shell down.</summary>
    StayDown,
}

/// <summary>
/// The <c>lifetime</c> file in the shell's config folder (<see cref="ConfigFolder"/>).
/// It is the shell's own word. Winland keeps a separate one, so stopping the shell does not read as Winland stopping.
/// </summary>
public static class ShellLifetime
{
    /// <summary>The file's name.</summary>
    public const string FileName = "lifetime";

    /// <summary>How long a watcher waits for the shell that was meant to replace the one that exited.</summary>
    public static readonly TimeSpan ReplaceLimit = TimeSpan.FromSeconds(30);

    /// <summary>The word as the file holds it.</summary>
    /// <param name="word">The word.</param>
    /// <returns><c>running</c>, <c>stopped</c>, or <c>replacing</c>.</returns>
    public static string Text(ShellLife word) => word switch
    {
        ShellLife.Stopped => "stopped",
        ShellLife.Replacing => "replacing",
        _ => "running",
    };

    /// <summary>Reads the file's text. Anything else, including a missing file, is <see cref="ShellLife.Running"/>.</summary>
    /// <param name="text">The file's text. Null when there is no file.</param>
    /// <returns>The word.</returns>
    public static ShellLife Read(string? text) => text?.Trim() switch
    {
        "stopped" => ShellLife.Stopped,
        "replacing" => ShellLife.Replacing,
        _ => ShellLife.Running,
    };

    /// <summary>What the watcher does once its shell has exited.</summary>
    /// <param name="word">The word the file held when the shell exited.</param>
    /// <param name="arrived">True when a shell holds the process now.</param>
    /// <param name="waited">How long ago the shell exited.</param>
    /// <returns>The step.</returns>
    public static ShellExit AfterExit(ShellLife word, bool arrived, TimeSpan waited)
    {
        if (arrived)
        {
            return ShellExit.Leave;
        }

        return word switch
        {
            ShellLife.Stopped => ShellExit.StayDown,
            ShellLife.Replacing => waited < ReplaceLimit ? ShellExit.Wait : ShellExit.StayDown,
            _ => ShellExit.Restart,
        };
    }

    /// <summary>The file in <paramref name="directory"/>.</summary>
    /// <param name="directory">The shell's config folder.</param>
    /// <returns>The full path.</returns>
    public static string PathIn(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Path.Combine(directory, FileName);
    }
}
