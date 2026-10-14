namespace Winshell.Core;

/// <summary>
/// What the shell offers an extension while it runs: the process folders, the merged settings,
/// and the ways an extension reaches the bar, the panel, and the desktop query.
/// </summary>
public interface IShellHost
{
    /// <summary>The program's folder, <see cref="AppContext.BaseDirectory"/>.</summary>
    string BaseDirectory { get; }

    /// <summary>The shell's config folder, <see cref="ConfigFolder.Path"/>.</summary>
    string ConfigDirectory { get; }

    /// <summary>The merged settings the shell started with.</summary>
    ShellConfig Config { get; }

    /// <summary>Runs <paramref name="work"/> on the UI thread. A call that is already on it runs at once.</summary>
    /// <param name="work">What to run.</param>
    void OnUi(Action work);

    /// <summary>Opens <paramref name="pluginId"/>'s panel, then runs <paramref name="command"/> when one was given.</summary>
    /// <param name="pluginId">Plugin id.</param>
    /// <param name="command">A panel command, or null to only open the panel.</param>
    /// <returns>The line the shell answers with, the same words <c>summon</c> prints.</returns>
    string Summon(string pluginId, string? command);

    /// <summary>Closes the open panel.</summary>
    /// <returns>The line the shell answers with, the same words <c>dismiss</c> prints.</returns>
    string Dismiss();

    /// <summary>Sends <paramref name="command"/> to a running plugin.</summary>
    /// <param name="pluginId">Plugin id.</param>
    /// <param name="command">The plugin command, without the <c>winshell:</c> prefix.</param>
    /// <param name="down">True on the key down, false on the key up.</param>
    void RunPlugin(string pluginId, string command, bool down);

    /// <summary>
    /// Installs the services plugins may bind. Null clears them.
    /// An extension calls this from <see cref="IShellExtension.Start"/>, before the shell mounts plugins.
    /// </summary>
    /// <param name="source">The services, or null.</param>
    void UseServices(IPluginServiceSource? source);

    /// <summary>Sets or clears an indicator the extension keeps. The owner is a <c>host:</c> name.</summary>
    /// <param name="owner">A <c>host:</c> name, such as <see cref="HostIndicator.IdleLock"/>.</param>
    /// <param name="indicator">The glyph, or null to remove it.</param>
    void SetIndicator(string owner, PluginIndicator? indicator);

    /// <summary>Shows a notice the extension owns. Called from any thread.</summary>
    /// <param name="notice">The toast.</param>
    void ShowNotice(ToastFace notice);

    /// <summary>
    /// Replaces the facts <paramref name="extensionName"/> adds to the desktop query.
    /// Null or empty clears them. Strings, booleans, and numbers are kept, the same as a plugin's facts.
    /// </summary>
    /// <param name="extensionName">The extension's <see cref="IShellExtension.Name"/>.</param>
    /// <param name="facts">Names and values, or null to clear.</param>
    void SetFacts(string extensionName, IReadOnlyDictionary<string, object?>? facts);

    /// <summary>The open panel's plugin id, or null when none is open. An extension reads this from <see cref="IShellExtension.Facts"/>.</summary>
    string? OpenPanelId { get; }

    /// <summary>
    /// Paints <paramref name="look"/> on the bar, panels, toasts, and pill.
    /// An extension calls this when the theme, the look, or Windows chrome changes.
    /// A shell that does not draw the surfaces ignores it.
    /// </summary>
    /// <param name="look">The look to paint.</param>
    void ApplyLook(SurfaceLook look)
    {
    }

    /// <summary>
    /// Installs the hands the shell calls for the keyboard, the Winland claim, and bar edits.
    /// Null clears them. An extension calls this from <see cref="IShellExtension.Start"/>, before plugins mount.
    /// </summary>
    /// <param name="hands">The hands, or null.</param>
    void UseHands(SurfaceHands? hands)
    {
    }

    /// <summary>Runs a Winland command. False when Winland is absent or refused it.</summary>
    /// <param name="command">The command word.</param>
    /// <param name="args">The words after it.</param>
    /// <param name="error">The sentence, when the answer is false.</param>
    /// <returns>True when Winland ran it.</returns>
    bool TryDispatch(string command, IReadOnlyList<string> args, out string error)
    {
        error = "Winland is not running";
        return false;
    }

    /// <summary>Reads a Winland query. False when Winland is absent.</summary>
    /// <param name="name">The query name.</param>
    /// <param name="json">True for JSON.</param>
    /// <param name="body">The body, when the answer is true.</param>
    /// <returns>True when Winland answered.</returns>
    bool TryQuery(string name, bool json, out string body)
    {
        body = "";
        return false;
    }

    /// <summary>Reads the shell's settings again and rebuilds the bars from them. An extension calls this after it writes the bar.</summary>
    void ReloadSettings()
    {
    }

    /// <summary>The shell's report: the open panel, the bars, and plugin facts. An extension adds its own facts beside this.</summary>
    /// <returns>An empty report when the shell has not drawn a desk.</returns>
    ShellReport Snapshot() => new(null, null, null, null);

    /// <summary>The open panel, including its rows and the monitor it is on. Null when none is open.</summary>
    ShellPanel? PanelView => null;

    /// <summary>The bar's thickness in physical pixels. Zero while the bar is hidden or the desk has no bar.</summary>
    int BarThickness => 0;

    /// <summary>True while the pointer is on a bar, so a move should ask <see cref="PointerMoved"/>.</summary>
    bool HoverArmed => false;

    /// <summary>Opens <paramref name="pluginId"/>'s panel, or closes it when that panel is already open.</summary>
    /// <param name="pluginId">Plugin id.</param>
    void Open(string pluginId)
    {
    }

    /// <summary>Left-clicks <paramref name="pluginId"/>'s bar item.</summary>
    /// <param name="pluginId">Plugin id.</param>
    /// <returns>False when that plugin is not running.</returns>
    bool Click(string pluginId) => false;

    /// <summary>Opens the numbered panel widget on the right, or closes it when that panel is already open.</summary>
    /// <param name="number">1-based index among the visible panel widgets. The tray is skipped.</param>
    void OpenRight(int number)
    {
    }

    /// <summary>Sends a panel command and opens nothing first, so the plugin decides whether its panel opens.</summary>
    /// <param name="pluginId">Plugin id.</param>
    /// <param name="command">The panel command.</param>
    /// <returns>False when that plugin is not running.</returns>
    bool SendPanel(string pluginId, string command) => false;

    /// <summary>Opens <paramref name="pluginId"/>'s panel at the middle of the bar it was last shown on.</summary>
    /// <param name="pluginId">Plugin id.</param>
    void OpenAtCenter(string pluginId)
    {
    }

    /// <summary>
    /// Opens <paramref name="pluginId"/>'s panel and sends a command, a value, text, or a key.
    /// Null when the panel is open. A sentence when it is not.
    /// </summary>
    /// <param name="pluginId">Plugin id.</param>
    /// <param name="command">The panel command. Empty sends none.</param>
    /// <param name="value">The number a slider or switch sends. Null sends none.</param>
    /// <param name="text">What a text field submits. Null when the command is not a field's.</param>
    /// <param name="key">An arrow key or Return. Null sends the command as text.</param>
    /// <returns>Null when the panel is open, or the sentence saying why not.</returns>
    string? SummonFields(string pluginId, string command, double? value, string? text, PanelKey? key) =>
        pluginId + " is not running";

    /// <summary>Sends a plugin command at one phase. A shell with no desk ignores it.</summary>
    /// <param name="pluginId">Plugin id.</param>
    /// <param name="command">The plugin command.</param>
    /// <param name="phase">A one-shot, or the edge of a held chord.</param>
    void RunPhase(string pluginId, string command, CommandPhase phase)
    {
    }

    /// <summary>The panel reads for <paramref name="pluginId"/>, or the built-in reads when this host did not start it.</summary>
    /// <param name="pluginId">The open panel's plugin id, or null.</param>
    /// <returns>The reads that should run while that panel is open.</returns>
    PanelReads ReadsOf(string? pluginId) => default;

    /// <summary>A physical Escape the hook kept. The plugin holding the claim hears it.</summary>
    void TakeEscape()
    {
    }

    /// <summary>Ends a plugin that stays loaded, so the next mount loads its new build. The factory's forget runs first.</summary>
    /// <param name="pluginId">Plugin id.</param>
    void Retire(string pluginId)
    {
    }

    /// <summary>Publishes the indicators the extension keeps, for the modes that changed.</summary>
    void PublishHost()
    {
    }

    /// <summary>Shows a notice with a title and a body. Called from any thread.</summary>
    /// <param name="title">The first line.</param>
    /// <param name="body">The line under it.</param>
    void ShowBrief(string title, string body)
    {
    }

    /// <summary>The pointer moved. A shell with no desk ignores it.</summary>
    void PointerMoved()
    {
    }

    /// <summary>Ticks the workspace icons now, ahead of the shell's own turn.</summary>
    void TickWorkspace()
    {
    }

    /// <summary>Looks at every bar's layer now.</summary>
    void SyncBars()
    {
    }

    /// <summary>Matches a bar to each monitor, and builds the items again when a monitor came or went.</summary>
    void FollowMonitors()
    {
    }

    /// <summary>Pushes the shell's state to Winland now. A lock callback relies on this returning after the push.</summary>
    void PushNow()
    {
    }

    /// <summary>
    /// Something happened. The bar loop returns to the fast pace when it was idle or away (ADR 0029).
    /// Safe from any thread. A shell with no desk ignores it.
    /// </summary>
    void Wake()
    {
    }

    /// <summary>
    /// The session locked or the display turned off. The shell gives memory back at once (ADR 0062).
    /// Safe from any thread.
    /// </summary>
    void NoteUnseen()
    {
    }

    /// <summary>
    /// The extension's plugin factory. Null uses the shell's own.
    /// An extension calls this from <see cref="IShellExtension.Start"/>, before plugins mount.
    /// </summary>
    /// <param name="create">Constructs a plugin, or returns null when it cannot.</param>
    /// <param name="forget">Drops a loaded copy so the next mount reads the file again. Null keeps the shell's own.</param>
    void UseFactory(Func<PluginManifest, IWinomaPlugin?>? create, Action<string>? forget)
    {
    }

    /// <summary>
    /// Closes the surfaces. It does not stop Winland: a shell stop leaves the window manager running,
    /// and a desktop stop asks Winland itself (<see cref="DeskStop"/>).
    /// The process may then exit. This does not call <see cref="IShellExtension.Stop"/>.
    /// </summary>
    /// <param name="replaced">True when another build takes this one's place, so the taskbar stays hidden.</param>
    void CloseDesk(bool replaced)
    {
    }
}

/// <summary>
/// Code the shell loads from a DLL named in <c>extensions</c> and starts with the process.
/// Winoma is one. A shell with none still draws a bar and hosts plugins.
/// </summary>
public interface IShellExtension
{
    /// <summary>A short name, such as <c>winoma</c>. Facts are keyed by it.</summary>
    string Name { get; }

    /// <summary>Windows this extension opens that are not a plugin's panel, such as Setup and About.</summary>
    IReadOnlyList<string> Surfaces { get; }

    /// <summary>
    /// Global names this extension answers, such as <c>winoma:menu</c>.
    /// The shell offers a name here before it treats the press as its own.
    /// </summary>
    IReadOnlyList<string> Globals { get; }

    /// <summary>Services this extension gives plugins, or null when it gives none.</summary>
    IPluginServiceSource? Services { get; }

    /// <summary>Called once on the UI thread after the shell is up and before plugins mount.</summary>
    /// <param name="host">The shell this extension is running inside.</param>
    void Start(IShellHost host);

    /// <summary>Called once on the UI thread as the shell stops. Plugins have already stopped.</summary>
    void Stop();

    /// <summary>Handles one global this extension owns.</summary>
    /// <param name="name">The global name, such as <c>winoma:menu</c>.</param>
    /// <param name="down">True on the key down, false on the key up.</param>
    /// <param name="repeat">True when the key is repeating.</param>
    /// <returns>True when this extension handled it.</returns>
    bool TryHandleGlobal(string name, bool down, bool repeat);

    /// <summary>
    /// Facts this extension adds to the desktop query right now, or null when it adds none.
    /// The shell also keeps whatever was last given to <see cref="IShellHost.SetFacts"/>.
    /// </summary>
    /// <returns>Names and values, or null.</returns>
    IReadOnlyDictionary<string, object?>? Facts();

    /// <summary>
    /// A Winland event that is not a plugin command. The shell runs <c>winshell:</c> binds itself, then offers the rest:
    /// a <c>winoma:</c> bind, the picker, activity, and the workspace, focus, windows, and placed pictures.
    /// </summary>
    /// <param name="kind">The event kind, such as <c>workspace</c> or <c>bind</c>.</param>
    /// <param name="body">The event body.</param>
    void OnWinland(string kind, string body)
    {
    }

    /// <summary>
    /// True when this extension fills the shell's bar, panels, toasts, and pills.
    /// The shell still owns the application and the Kit brushes. It calls <see cref="OpenDesktop"/>
    /// on the UI thread and does not open its own desk. It does not call <see cref="Start"/> on this extension.
    /// Other extensions are started first.
    /// </summary>
    bool FillsSurfaces => false;

    /// <summary>
    /// Fills the shell's surfaces from inside the shell's application.
    /// Called on the UI thread. Return once the desktop is opening. The shell's message loop stays up.
    /// </summary>
    /// <param name="host">The shell this extension is running inside.</param>
    void OpenDesktop(IShellHost host)
    {
    }

    /// <summary>
    /// Puts back what this extension took from Windows, such as Win+L and the night-light ramp.
    /// The shell has already shown the taskbar and restored the pointer.
    /// </summary>
    void GiveBack()
    {
    }

    /// <summary>
    /// Opens a window this extension owns, such as Setup or About.
    /// False when <paramref name="id"/> is a plugin panel, which the shell opens itself.
    /// </summary>
    /// <param name="id">The surface id.</param>
    /// <param name="command">A command to send, or null.</param>
    /// <returns>True when this extension opened it.</returns>
    bool TrySummon(string id, string? command) => false;
}
