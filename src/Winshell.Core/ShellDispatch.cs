namespace Winshell.Core;

/// <summary>
/// Turns one <c>winshell</c> line into the reply both hosts print.
/// The shell with no session and a loaded session use the same words.
/// </summary>
public static class ShellDispatch
{
    /// <summary>
    /// Answers <paramref name="line"/>. <paramref name="summon"/>, <paramref name="dismiss"/>, <paramref name="plugin"/>,
    /// <paramref name="report"/>, and <paramref name="stop"/> run only for the matching request, on the caller's thread.
    /// </summary>
    /// <param name="line">One line, without the newline.</param>
    /// <param name="summon">Opens a plugin's panel, then runs the command when one was given. Returns the reply.</param>
    /// <param name="dismiss">Closes the open panel. Returns the reply.</param>
    /// <param name="plugin">Sends a plugin command.</param>
    /// <param name="report">The open panel, the bars, and the facts, read at the moment of the request.</param>
    /// <param name="stop">Stops the shell. The reply is sent before this returns to the client.</param>
    /// <returns><c>ok</c>, <c>dismissed</c>, <c>stopped</c>, the state, or why the line was refused.</returns>
    public static string Reply(
        string? line,
        Func<string, string?, string> summon,
        Func<string> dismiss,
        Action<string, string, CommandPhase> plugin,
        Func<ShellReport> report,
        Action stop)
    {
        ArgumentNullException.ThrowIfNull(summon);
        ArgumentNullException.ThrowIfNull(dismiss);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(stop);
        if (!ShellWords.TryParse(line, out var word, out var error) || word is null)
        {
            return error;
        }

        switch (word)
        {
            case ShellWord.Summon summonWord:
                return summon(summonWord.Id, summonWord.Command);
            case ShellWord.Dismiss:
                return dismiss();
            case ShellWord.Plugin pluginWord:
                plugin(pluginWord.Id, pluginWord.Command, pluginWord.Phase);
                return "ok";
            case ShellWord.State state:
                var snapshot = report();
                return state.Json ? snapshot.Json() : snapshot.Text();
            case ShellWord.Stop:
                stop();
                return "stopped";
            default:
                return error;
        }
    }
}
