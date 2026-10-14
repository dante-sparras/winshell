namespace Winshell.Core;

/// <summary>
/// One card as the host draws it, in the notification panel and as a toast: the program's logo and name,
/// the time, a title, the body, an optional picture and image, and its buttons.
/// </summary>
public sealed record ToastCard
{
    /// <summary>The widest a toast or a notification card is drawn, in device-independent pixels.</summary>
    public const int PanelWidth = 440;

    /// <summary>The largest picture file a card draws, in bytes. A larger one is left out.</summary>
    public const long LargestPicture = 4 * 1024 * 1024;

    /// <summary>The program's name.</summary>
    public string App { get; init; } = "";

    /// <summary>The program's logo, as image bytes. Null draws a bell.</summary>
    public byte[]? AppIcon { get; init; }

    /// <summary>When it arrived, as the card shows it, such as <c>5m ago</c>.</summary>
    public string Time { get; init; } = "";

    /// <summary>The first line, in bold.</summary>
    public string Title { get; init; } = "";

    /// <summary>The rest of the text.</summary>
    public string Body { get; init; } = "";

    /// <summary>
    /// A round picture beside the text, such as a sender's avatar: the full path of an image file, which the host reads
    /// when it draws the card. Empty draws none, and so does a file that is gone, larger than
    /// the largest picture the host will read, or not an image.
    /// </summary>
    public string Picture { get; init; } = "";

    /// <summary>A wide image under the text: the full path of an image file, read as <see cref="Picture"/> is. Empty draws none.</summary>
    public string Image { get; init; } = "";

    /// <summary>Buttons under the text. A click sends the chip's command.</summary>
    public IReadOnlyList<PanelChip> Actions { get; init; } = [];

    /// <summary>What a click on the card sends. Empty makes the card inert.</summary>
    public string Command { get; init; } = "";

    /// <summary>What the close button sends. Empty draws no close button.</summary>
    public string Dismiss { get; init; } = "";

    /// <summary>True draws the program's logo and name over the text. A card under its program's heading in the panel leaves them out.</summary>
    public bool ShowApp { get; init; } = true;
}

/// <summary>
/// A toast a plugin shows with <see cref="IPluginContext.ShowToast"/>. The host draws it at the outer corner of every monitor's Bar
/// (<see cref="ToastMonitors"/>), slides it in, and slides it out after <see cref="Duration"/>. The pointer resting on it on any monitor holds it.
/// Up to three stack, the newest nearest the bar. It never takes focus.
/// One that <see cref="Stays"/> has no time: it leaves when it is dismissed or hidden, and a newer toast does not push it out.
/// </summary>
public sealed record ToastFace
{
    /// <summary>The default time on screen.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(5);

    /// <summary>The time on screen for a toast its program asked to last, such as a reminder: as long as Windows' long banner.</summary>
    public static readonly TimeSpan LongDuration = TimeSpan.FromSeconds(25);

    /// <summary>How many toasts are on screen at once.</summary>
    public const int MostShown = 3;

    /// <summary>True for a toast that stays until it is dismissed, such as an incoming call. <see cref="Duration"/> is not used.</summary>
    public bool Stays { get; init; }

    /// <summary>
    /// Makes room for a new toast among the ones on screen. With fewer than <see cref="MostShown"/> there is room.
    /// Otherwise the oldest that does not stay leaves. When every one of them stays, the new toast is not shown:
    /// its notification waits in the panel, and a ringing call is not pushed off screen.
    /// </summary>
    /// <param name="stays">Whether each toast on screen stays, newest first.</param>
    /// <param name="leaves">The index of the toast that leaves, or -1 when none has to.</param>
    /// <returns>False when the new toast is not shown.</returns>
    public static bool Admit(IReadOnlyList<bool> stays, out int leaves)
    {
        ArgumentNullException.ThrowIfNull(stays);
        leaves = -1;
        if (stays.Count < MostShown)
        {
            return true;
        }

        for (var i = stays.Count - 1; i >= 0; i--)
        {
            if (!stays[i])
            {
                leaves = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Names this toast. Showing the same key again replaces it.</summary>
    public string Key { get; init; } = "";

    /// <summary>What it shows. A click, a button, and the close button send their commands to the plugin's panel handler.</summary>
    public ToastCard Card { get; init; } = new();

    /// <summary>How long it stays before it slides out.</summary>
    public TimeSpan Duration { get; init; } = DefaultDuration;
}
