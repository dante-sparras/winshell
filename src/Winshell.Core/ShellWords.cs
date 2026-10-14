namespace Winshell.Core;

/// <summary>One line a <c>winshell</c> client sends the shell.</summary>
public abstract record ShellWord
{
    private ShellWord()
    {
    }

    /// <summary>Open a plugin's panel, then run <paramref name="Command"/> when one was given.</summary>
    /// <param name="Id">Plugin id.</param>
    /// <param name="Command">A panel command, or null to only open the panel.</param>
    public sealed record Summon(string Id, string? Command) : ShellWord;

    /// <summary>Close the open panel.</summary>
    public sealed record Dismiss : ShellWord;

    /// <summary>Send a plugin command.</summary>
    /// <param name="Id">Plugin id.</param>
    /// <param name="Command">The command the manifest declares.</param>
    /// <param name="Phase">A one-shot, or the edge of a held chord.</param>
    public sealed record Plugin(string Id, string Command, CommandPhase Phase) : ShellWord;

    /// <summary>The open panel, the bars, and plugin facts.</summary>
    /// <param name="Json">True when the reply is JSON.</param>
    public sealed record State(bool Json) : ShellWord;

    /// <summary>Stop the shell.</summary>
    public sealed record Stop : ShellWord;
}

/// <summary>
/// Reads a <c>winshell</c> line: <c>summon</c>, <c>dismiss</c>, <c>plugin.run</c> (or <c>plugin run</c>), <c>state</c>, and <c>stop</c>.
/// </summary>
public static class ShellWords
{
    /// <summary>The line a client sees when it sent nothing the shell can read.</summary>
    public const string Usage = "Usage: summon <id> [<command>] | dismiss | plugin.run <id> <command> [down|up] | state [-j] | stop";

    /// <summary>Reads <paramref name="line"/>.</summary>
    /// <param name="line">One line, without the newline.</param>
    /// <param name="word">The request, when the answer is true.</param>
    /// <param name="error">Why the line was refused, when the answer is false.</param>
    /// <returns>False when the line is not a request.</returns>
    public static bool TryParse(string? line, out ShellWord? word, out string error)
    {
        word = null;
        error = "";
        if (string.IsNullOrWhiteSpace(line))
        {
            error = Usage;
            return false;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (parts[0])
        {
            case "summon":
                return Summon(parts, out word, out error);
            case "dismiss":
                return One(parts, new ShellWord.Dismiss(), out word, out error);
            case "state":
                return State(parts, out word, out error);
            case "stop":
                return One(parts, new ShellWord.Stop(), out word, out error);
            case "plugin.run":
                return Plugin(parts, from: 1, out word, out error);
            case "plugin" when parts.Length > 1 && parts[1] == "run":
                return Plugin(parts, from: 2, out word, out error);
            default:
                error = "Unknown command '" + parts[0] + "'.";
                return false;
        }
    }

    private static bool One(string[] parts, ShellWord parsed, out ShellWord? word, out string error)
    {
        word = null;
        error = "";
        if (parts.Length != 1)
        {
            error = parts[0] + " takes no arguments.";
            return false;
        }

        word = parsed;
        return true;
    }

    private static bool Summon(string[] parts, out ShellWord? word, out string error)
    {
        word = null;
        error = "";
        if (parts.Length is < 2 or > 3)
        {
            error = "Usage: summon <id> [<command>]";
            return false;
        }

        word = new ShellWord.Summon(parts[1], parts.Length == 3 ? parts[2] : null);
        return true;
    }

    private static bool State(string[] parts, out ShellWord? word, out string error)
    {
        word = null;
        error = "";
        if (parts.Length == 1)
        {
            word = new ShellWord.State(Json: false);
            return true;
        }

        if (parts.Length == 2 && parts[1] is "-j" or "json")
        {
            word = new ShellWord.State(Json: true);
            return true;
        }

        error = "Usage: state [-j]";
        return false;
    }

    private static bool Plugin(string[] parts, int from, out ShellWord? word, out string error)
    {
        word = null;
        error = "";
        var count = parts.Length - from;
        if (count is < 2 or > 3)
        {
            error = "Usage: plugin.run <id> <command> [down|up]";
            return false;
        }

        var phase = CommandPhase.Run;
        if (count == 3)
        {
            if (parts[from + 2] == "down")
            {
                phase = CommandPhase.Down;
            }
            else if (parts[from + 2] == "up")
            {
                phase = CommandPhase.Up;
            }
            else
            {
                error = "Usage: plugin.run <id> <command> [down|up]";
                return false;
            }
        }

        word = new ShellWord.Plugin(parts[from], parts[from + 1], phase);
        return true;
    }
}
