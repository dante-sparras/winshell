namespace Winshell.Core;

/// <summary>
/// A global bind's name, as Winland stores it and the shell reads it.
/// A plugin command is <c>winshell:&lt;plugin&gt;:&lt;command&gt;</c>. Anything an extension does is <c>winoma:&lt;id&gt;</c>.
/// </summary>
public static class ShellGlobal
{
    /// <summary>The prefix of a plugin command.</summary>
    public const string WinshellPrefix = "winshell:";

    /// <summary>The prefix of a command an extension runs.</summary>
    public const string WinomaPrefix = "winoma:";

    /// <summary>
    /// Reads <c>winshell:&lt;plugin&gt;:&lt;command&gt;</c>. The command is the text after the last colon.
    /// The plugin id may itself contain colons. The command is letters, digits, <c>-</c>, <c>_</c>, and <c>.</c>.
    /// </summary>
    /// <param name="name">A global name.</param>
    /// <param name="plugin">The plugin id.</param>
    /// <param name="command">The command name.</param>
    /// <returns>False when <paramref name="name"/> is not a plugin global.</returns>
    public static bool TryPlugin(string? name, out string plugin, out string command)
    {
        plugin = "";
        command = "";
        if (name is null || !name.StartsWith(WinshellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = name[WinshellPrefix.Length..];
        var colon = rest.LastIndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1)
        {
            return false;
        }

        var left = rest[..colon];
        var right = rest[(colon + 1)..];
        if (!IsWord(left) || !IsCommand(right))
        {
            return false;
        }

        plugin = left;
        command = right;
        return true;
    }

    /// <summary>Reads <c>winoma:&lt;id&gt;</c>. The id is everything after the prefix and may contain colons.</summary>
    /// <param name="name">A global name.</param>
    /// <param name="id">The extension's command id.</param>
    /// <returns>False when <paramref name="name"/> is not an extension global, or the id is empty or holds whitespace.</returns>
    public static bool TryWinoma(string? name, out string id)
    {
        id = "";
        if (name is null || !name.StartsWith(WinomaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = name[WinomaPrefix.Length..];
        if (!IsWord(rest))
        {
            return false;
        }

        id = rest;
        return true;
    }

    private static bool IsWord(string text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    private static bool IsCommand(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
