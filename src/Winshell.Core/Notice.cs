namespace Winshell.Core;

/// <summary>
/// A Notice: a card a Chord shows when asked, such as the time, the weather, or the battery.
/// It is a toast with no notification behind it, so the bell does not list or count it, and do not disturb does not hold it back.
/// The plugin that owns the Chord shows it with <see cref="IPluginContext.ShowToast"/>.
/// </summary>
public static class Notice
{
    /// <summary>The command each owning plugin declares for its Notice, so its Chord is <c>plugin:&lt;id&gt;:notice</c>.</summary>
    public const string Command = "notice";

    /// <summary>The toast key. A plugin has one Notice, so showing it again replaces the one on screen.</summary>
    public const string Key = "notice";

    /// <summary>The owner of a Notice the Host shows itself, for a Chord no plugin owns. A <c>host:</c> name, which no plugin id can be.</summary>
    public const string Host = HostIndicator.Prefix + "notice";

    /// <summary>What the card's close button sends the plugin's panel handler. The host sends it too when it could not show the card.</summary>
    public const string Close = "notice-close";

    /// <summary>
    /// Hides the plugin's Notice when <paramref name="command"/> is <see cref="Close"/>. A plugin that shows a Notice calls this
    /// first in its panel handler.
    /// </summary>
    /// <param name="context">The plugin's context.</param>
    /// <param name="command">The command the panel handler was sent.</param>
    /// <returns>True when the command was <see cref="Close"/>, so the handler has nothing more to do with it.</returns>
    public static bool Closes(IPluginContext context, string command)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (command != Close)
        {
            return false;
        }

        context.HideToasts(Key);
        return true;
    }

    /// <summary>A Notice that leaves as a toast does, after <see cref="ToastFace.DefaultDuration"/>.</summary>
    /// <param name="title">The first line, in bold.</param>
    /// <param name="body">The lines under it. Empty draws none.</param>
    /// <returns>The toast to show.</returns>
    public static ToastFace Brief(string title, string body = "") => new() { Key = Key, Card = Card(title, body) };

    /// <summary>A Notice that stays until its plugin hides it: on the Chord pressed again, or on <see cref="Close"/>.</summary>
    /// <param name="title">The first line, in bold.</param>
    /// <param name="body">The lines under it. Empty draws none.</param>
    /// <returns>The toast to show.</returns>
    public static ToastFace Staying(string title, string body = "") => new() { Key = Key, Card = Card(title, body), Stays = true };

    private static ToastCard Card(string title, string body)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);
        return new ToastCard { Title = title, Body = body, ShowApp = false, Dismiss = Close };
    }
}

/// <summary>
/// Whether a plugin's Notice that stays is on screen. Its Chord shows it, and the same Chord hides it.
/// The host never tells a plugin that a toast is gone, except through <see cref="Notice.Close"/>, so the plugin keeps count here.
/// </summary>
public sealed class NoticeLatch
{
    /// <summary>True while the Notice is on screen.</summary>
    public bool Up { get; private set; }

    /// <summary>The Chord was pressed.</summary>
    /// <returns>True when the Notice is to be shown, false when it is to be hidden.</returns>
    public bool Press()
    {
        Up = !Up;
        return Up;
    }

    /// <summary>The Notice left without the Chord: its close button was pressed, or the host could not show it.</summary>
    public void Closed() => Up = false;
}
