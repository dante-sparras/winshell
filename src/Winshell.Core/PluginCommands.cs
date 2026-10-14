
namespace Winshell.Core;

/// <summary>How a plugin command arrived.</summary>
public enum CommandPhase
{
    /// <summary>A one-shot: a chord, a click, or <c>winoma plugin run</c>.</summary>
    Run,

    /// <summary>A held chord went down.</summary>
    Down,

    /// <summary>A held chord came up.</summary>
    Up,
}

/// <summary>A plugin that owns the commands its manifest declares.</summary>
public interface ICommandPlugin
{
    /// <summary>Runs <paramref name="name"/>. Called on the UI thread.</summary>
    /// <param name="name">A command from the manifest's <c>commands</c>.</param>
    /// <param name="phase"><see cref="CommandPhase.Run"/>, or the edge of a held chord.</param>
    void OnCommand(string name, CommandPhase phase);
}

/// <summary>A plugin that can take Escape while it has claimed it with <see cref="IPluginContext.ClaimEscape"/>.</summary>
public interface IEscapePlugin
{
    /// <summary>A physical Escape went down while this plugin held the claim. The focused window did not see it. Called on the UI thread.</summary>
    void OnEscape();
}

/// <summary>One command a manifest declares.</summary>
/// <param name="Name">The word after the plugin id, such as <c>toggle</c>.</param>
/// <param name="Summary">One sentence for <c>winoma plugin commands</c>.</param>
/// <param name="Hold">True when a chord bound to it is held: the plugin hears the key go down and come up.</param>
public sealed record PluginCommandInfo(string Name, string Summary, bool Hold);

/// <summary>A chord a manifest binds by default. It is written into a new <c>bindings.json</c>.</summary>
/// <param name="Chord">Chord text, such as <c>Super+Ctrl+X</c>.</param>
/// <param name="Command">A <see cref="PluginCommandInfo.Name"/> from the same manifest.</param>
public sealed record PluginChord(string Chord, string Command);

/// <summary>The <c>plugin:</c> command ids in <c>bindings.json</c>.</summary>
public static class PluginCommandIds
{
    /// <summary>The prefix of a plugin command id.</summary>
    public const string Prefix = "plugin:";

    /// <summary>The id <c>bindings.json</c> stores for <paramref name="name"/> on <paramref name="plugin"/>.</summary>
    /// <param name="plugin">Plugin id, such as <c>winoma.dictation</c>.</param>
    /// <param name="name">Command name, such as <c>toggle</c>.</param>
    /// <returns><c>plugin:winoma.dictation:toggle</c>.</returns>
    public static string Of(string plugin, string name) => Prefix + plugin + ":" + name;

    /// <summary>
    /// Reads <c>plugin:&lt;id&gt;:&lt;name&gt;</c>. The name is after the last colon.
    /// Neither part may be empty or hold whitespace.
    /// </summary>
    /// <param name="id">A command id.</param>
    /// <param name="plugin">The plugin id.</param>
    /// <param name="name">The command name.</param>
    /// <returns>False when <paramref name="id"/> is not a plugin command.</returns>
    public static bool TryParse(string? id, out string plugin, out string name)
    {
        plugin = "";
        name = "";
        if (id is null || !id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = id[Prefix.Length..];
        var colon = rest.LastIndexOf(':');
        if (colon <= 0 || colon == rest.Length - 1)
        {
            return false;
        }

        var left = rest[..colon];
        var right = rest[(colon + 1)..];
        if (!IsWord(left) || !IsName(right))
        {
            return false;
        }

        plugin = left;
        name = right;
        return true;
    }

    /// <summary>True when <paramref name="name"/> can be a command name: letters, digits, <c>-</c>, <c>_</c>, and <c>.</c>.</summary>
    /// <param name="name">A candidate name.</param>
    /// <returns>True for a usable name.</returns>
    public static bool IsName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The command a manifest declares under <paramref name="plugin"/> and <paramref name="name"/>.
    /// A clone whose <see cref="PluginManifest.ClonedFrom"/> is <paramref name="plugin"/> answers for it.
    /// Null when no manifest in <paramref name="plugins"/> has that id, or it does not declare that name.
    /// </summary>
    /// <param name="plugins">Discovered manifests.</param>
    /// <param name="plugin">Plugin id.</param>
    /// <param name="name">Command name.</param>
    /// <returns>The declaration, or null.</returns>
    public static PluginCommandInfo? Find(IReadOnlyList<PluginManifest> plugins, string plugin, string name)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        foreach (var manifest in plugins)
        {
            if (!manifest.Id.Equals(plugin, StringComparison.Ordinal)
                && !(manifest.ClonedFrom.Length > 0 && manifest.ClonedFrom.Equals(plugin, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var command in manifest.Commands)
            {
                if (command.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
            }
        }

        return null;
    }

    private static bool IsWord(string text)
    {
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return text.Length > 0;
    }
}
