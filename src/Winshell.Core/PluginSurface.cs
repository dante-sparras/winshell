namespace Winshell.Core;

/// <summary>
/// The bar's half of what a running plugin reaches outside its slot: the open panel, the pill, and toasts.
/// <see cref="PluginRuntime"/> keeps the other half: Escape, the indicator cluster, and facts.
/// </summary>
public interface IPluginSurface
{
    /// <summary>True while <paramref name="id"/>'s panel is the one on screen.</summary>
    /// <param name="id">Plugin id.</param>
    /// <returns>True when that panel is open.</returns>
    bool PanelOpen(string id);

    /// <summary>Closes the open panel.</summary>
    void DismissPanel();

    /// <summary>Shows, changes, or hides <paramref name="id"/>'s pill. Called from any thread.</summary>
    /// <param name="id">Plugin id.</param>
    /// <param name="face">What the pill shows.</param>
    void ShowPill(string id, PillFace face);

    /// <summary>Adds an input level to <paramref name="id"/>'s live pill. Called from any thread, many times a second.</summary>
    /// <param name="id">Plugin id.</param>
    /// <param name="level">Level from 0 to 1.</param>
    void FeedPill(string id, float level);

    /// <summary>Shows or replaces one of <paramref name="id"/>'s toasts. Called from any thread.</summary>
    /// <param name="id">Plugin id.</param>
    /// <param name="toast">What the toast shows.</param>
    void ShowToast(string id, ToastFace toast);

    /// <summary>Slides <paramref name="id"/>'s toast with <paramref name="key"/> out, or all of them when the key is null. Called from any thread.</summary>
    /// <param name="id">Plugin id.</param>
    /// <param name="key">The toast's key, or null for all.</param>
    void HideToasts(string id, string? key);

    /// <summary>A plugin set or cleared its glyph in the indicator cluster. Called from any thread.</summary>
    void IndicatorsChanged();

    /// <summary>
    /// A mount stopped the plugins <paramref name="running"/> no longer names.
    /// Hides the pill of a plugin that stopped, whether it was <paramref name="escapeOwner"/> or held no Escape, and drops the toasts of each plugin that stopped.
    /// </summary>
    /// <param name="escapeOwner">The stopped plugin that held Escape, or null.</param>
    /// <param name="running">True for a plugin id that still runs.</param>
    void Forget(string? escapeOwner, Func<string, bool> running);

    /// <summary>Holds the open panel up across an elevation prompt. See <see cref="IPluginContext.HoldDismiss"/>.</summary>
    /// <param name="hold">True to hold it, false to release the hold.</param>
    void HoldDismiss(bool hold)
    {
    }

    /// <summary>Runs <paramref name="work"/> on a later turn of the UI thread.</summary>
    /// <param name="work">What to run.</param>
    void After(Action work)
    {
    }

    /// <summary>Gives the open panel the keyboard.</summary>
    void TakeKeyboard()
    {
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard. See <see cref="IPluginContext.CopyText"/>.</summary>
    /// <param name="text">What to place.</param>
    void CopyText(string text)
    {
    }

    /// <summary>Opens a file with the program Windows has for it. See <see cref="IPluginContext.OpenPath"/>.</summary>
    /// <param name="path">The file's full path.</param>
    void OpenPath(string path)
    {
    }
}

/// <summary>A plugin's slot on the bar: its face and the panel that opens from it.</summary>
public interface IPluginSlot
{
    /// <summary>Draws <paramref name="face"/> in the slot.</summary>
    /// <param name="face">What the bar should draw.</param>
    void SetFace(BarFace face);

    /// <summary>
    /// Opens the Kit panel under this slot (ADR 0045). The host builds it from <paramref name="plugin"/>, which draws it with the Kit,
    /// and keeps it current on every tick while it is open. An open panel stays as it is.
    /// </summary>
    /// <param name="plugin">The plugin that owns the slot, as it was created.</param>
    void OpenPanel(IWinomaPlugin plugin);

    /// <summary>
    /// Sends the slot's gestures to <paramref name="input"/>, and lets <paramref name="plugin"/> draw its own item when it does (ADR 0045).
    /// Called once, after it attached.
    /// </summary>
    /// <param name="input">Where the slot's gestures go. It marks the plugin due before passing each one on.</param>
    /// <param name="plugin">The plugin that owns the slot, as it was created.</param>
    void Bind(IWinomaPlugin input, IWinomaPlugin plugin);
}

/// <summary>Serves each service family to the plugins that may bind it.</summary>
public interface IPluginServiceSource
{
    /// <summary>
    /// True when this shell has the services a first-party plugin binds.
    /// False on a shell with no extension: those plugins are skipped, and a sheet still starts.
    /// </summary>
    bool Available { get; }

    /// <summary>The object that serves <typeparamref name="TServices"/>, or null when nothing does.</summary>
    /// <typeparam name="TServices">A service family interface.</typeparam>
    /// <returns>The services, or null.</returns>
    TServices? Services<TServices>()
        where TServices : class;
}

/// <summary>A plugin to start on the bar.</summary>
/// <param name="Manifest">Its manifest.</param>
/// <param name="Slot">Its bar entry.</param>
/// <param name="View">Where it draws.</param>
public sealed record PluginStart(PluginManifest Manifest, BarSlot Slot, IPluginSlot View);
