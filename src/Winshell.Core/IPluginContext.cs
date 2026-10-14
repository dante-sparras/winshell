namespace Winshell.Core;

/// <summary>What the host gives a plugin when it attaches.</summary>
public interface IPluginContext
{
    /// <summary>Plugin id.</summary>
    string Id { get; }

    /// <summary>The bar entry for this plugin. Schema values are on <see cref="BarSlot.Settings"/>.</summary>
    BarSlot Slot { get; }

    /// <summary>True when the bar is on the left or right edge.</summary>
    bool Vertical { get; }

    /// <summary>True while this plugin's panel is the one on screen.</summary>
    bool PanelOpen { get; }

    /// <summary>Publishes what the bar should draw.</summary>
    void SetFace(BarFace face);

    /// <summary>
    /// Opens this plugin's Kit panel, or leaves it open (ADR 0045). The host builds it with the plugin's <c>BuildPanel</c>,
    /// and on every tick while it is open reads the getters its controls were given and changes only what moved.
    /// </summary>
    void OpenPanel();

    /// <summary>Closes the panel.</summary>
    void DismissPanel();

    /// <summary>
    /// Takes or gives back Escape. While this plugin holds the claim, a physical Escape is kept from the focused window
    /// and reaches <see cref="IEscapePlugin.OnEscape"/>. Safe to call from any thread; the claim holds from the call, not from the next frame.
    /// </summary>
    /// <param name="claimed">True to take Escape, false to give it back.</param>
    void ClaimEscape(bool claimed);

    /// <summary>Shows, changes, or hides the floating pill. Safe to call from any thread.</summary>
    /// <param name="face">What the pill should show. <see cref="PillFace.Hidden"/> hides it.</param>
    void ShowPill(PillFace face);

    /// <summary>Adds an input level to a <see cref="PillState.Live"/> pill. Safe to call from any thread, many times a second.</summary>
    /// <param name="level">Level from 0 to 1.</param>
    void FeedPill(float level);

    /// <summary>
    /// Shows a toast at the outer corner of every monitor's Bar, or replaces the one with the same key. It slides out after its duration, from every monitor together.
    /// Its commands reach <see cref="IWinomaPlugin.OnPanel"/>. Safe to call from any thread.
    /// </summary>
    /// <param name="toast">What the toast shows.</param>
    void ShowToast(ToastFace toast);

    /// <summary>Slides this plugin's toast with <paramref name="key"/> out, or every one of its toasts when the key is null. Safe to call from any thread.</summary>
    /// <param name="key">The toast's key, or null for all.</param>
    void HideToasts(string? key);

    /// <summary>Sets or clears this plugin's glyph in the indicator cluster. Safe to call from any thread.</summary>
    /// <param name="indicator">The glyph, or null to remove it.</param>
    void SetIndicator(PluginIndicator? indicator);

    /// <summary>
    /// Publishes facts <c>winoma desktop</c> prints under <c>plugins</c> and this plugin's id.
    /// Strings, booleans, and numbers are kept. Null clears them. Safe to call from any thread.
    /// </summary>
    /// <param name="facts">Names and values.</param>
    void SetFacts(IReadOnlyDictionary<string, object>? facts);

    /// <summary>
    /// Keeps the open panel up while an elevation prompt this plugin asked for is on screen.
    /// A deactivation or a press outside does not close it until the hold is released.
    /// The release belongs on a later turn, so a deactivation queued by the prompt still sees the hold.
    /// </summary>
    /// <param name="hold">True to hold the panel open, false to let a deactivation close it again.</param>
    void HoldDismiss(bool hold)
    {
    }

    /// <summary>Runs <paramref name="work"/> on a later turn of the panel's thread.</summary>
    /// <param name="work">What to run.</param>
    void After(Action work)
    {
    }

    /// <summary>Gives the open panel the keyboard.</summary>
    void TakeKeyboard()
    {
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard, for a panel row that copies something. The host marks nothing as private: this is for text the person asked to copy.</summary>
    /// <param name="text">What to place. Empty places nothing.</param>
    void CopyText(string text)
    {
    }

    /// <summary>Asks the Host to open a file with the program Windows has for it, as a double click would. For a file the person was told about, such as one that arrived.</summary>
    /// <param name="path">The file's full path.</param>
    void OpenPath(string path)
    {
    }
}
