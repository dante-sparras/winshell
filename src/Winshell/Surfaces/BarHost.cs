using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Winshell;

/// <summary>
/// The plugin host: the one for the whole desk, however many Bars there are (ADR 0080). <see cref="PluginRuntime"/> runs the plugins;
/// this host draws what a plugin reaches outside its slot: its face, its panel, the pill, and its toasts. Each monitor's Bar has
/// a <see cref="BarView"/> here with its own copy of every item, and a plugin's one face is drawn on each through a <see cref="SlotFan"/>.
/// </summary>
internal sealed class BarHost
{
    // One for each Bar, the first Bar's first. A view lives as long as its window, from mount to mount.
    private List<BarView> _views = [];

    // The bar items whose plugin draws no panel, such as the indicator cluster. A summon has nothing to open for one, and sends its command as it stands.
    private readonly HashSet<string> _bare = new(StringComparer.Ordinal);
    private Func<bool>? _idleLock;
    private bool? _idleLockShown;
    private Func<bool>? _recording;
    private Func<string, bool>? _recordingAct;
    private bool? _recordingShown;
    private readonly List<CommandSlot> _commands = [];
    private readonly PluginRuntime _runtime;
    private readonly PanelRoster _roster;

    // .NET keeps the time zone it read first. The watch forgets it when Windows names another, which is cheaper than forgetting it every minute.
    private readonly ZoneWatch _zone = new(ShellZone.Key, TimeZoneInfo.ClearCachedData);
    private Func<bool>? _paused;
    private Action? _activity;
    private PluginPill? _pill;
    private NotificationToasts? _toasts;
    private Microsoft.UI.Dispatching.DispatcherQueue? _queue;
    private readonly PanelSession _session;
    private readonly PanelWindow _panel;
    private BarEdge _edge;

    /// <summary>The open panel's plugin id, or null when none is open.</summary>
    public string? OpenPanelId => _session.OpenId;

    /// <summary>The panel on screen, which every Bar's items press and read.</summary>
    internal PanelSession Session => _session;

    // True while the pointer is on a Bar. Read on the mouse hook's thread, which signals the hover check only then.
    private volatile bool _hoverArmed;
    private Action<string?>? _onPanel;
    private Action<BarEdge>? _edgeChosen;
    private Action<string, BarDropTarget>? _itemMoved;
    private Action? _transparentToggled;
    private Func<ShellRect>? _keyboard;

    // The Bar a click that no item's own gesture carries came from, for as long as that click is handled: a right click on the Bar itself,
    // or the Bar a Panel is opened on again after the Bars moved. A Panel opens against it.
    private BarView? _clicked;

    // The Bar the open Panel was last shown against.
    private BarView? _shown;

    // What the Panel window was last told about its layer. It starts above, as the window does.
    private bool _panelAbove = true;
    private Rect? _at;

    public BarHost()
    {
        _panel = new PanelWindow();
        _session = _panel.Session;
        _session.OpenChanged += MarkOpen;
        _runtime = new PluginRuntime(new HostSurface(this));
        _roster = new PanelRoster(_runtime, _session);
        _panel.CyclePanel += _roster.Cycle;
        _panel.UseBars(OnBar);
    }

    /// <summary>True when a screen point, in physical pixels, is inside a Bar's window, any monitor's.</summary>
    private bool OnBar(int x, int y)
    {
        foreach (var view in _views)
        {
            if (PointerProbe.TryWindow(view.Owner.Hwnd, out var rect) && rect.Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How the host learns which monitor has the keyboard, for a Panel or the Menu that no click on a Bar opened.</summary>
    /// <param name="monitor">The whole monitor with the keyboard, in physical pixels, as the shell names it.</param>
    public void UseKeyboard(Func<ShellRect> monitor) => _keyboard = monitor;

    private Func<ShellRect, int>? _monitorAt;

    /// <summary>Says which monitor a Bar's screen is, as <c>winoma desktop</c> numbers them, so each Bar marks the Workspace in front on its own (ADR 0080).</summary>
    public void UseMonitors(Func<ShellRect, int> at) => _monitorAt = at;

    /// <summary>The Bar on the monitor with the keyboard, as an index into the Bars. The first before the shell is asked.</summary>
    private int KeyboardBar() =>
        _keyboard is { } monitor && _views.Count > 1 ? MonitorOf(_views.ConvertAll(view => view.Owner.Screen), monitor()) : 0;

    /// <summary>Which Bar is on <paramref name="monitor"/>. An exact rectangle wins. Otherwise the Bar that covers the most of it. None at all is the first.</summary>
    private static int MonitorOf(IReadOnlyList<ShellRect> bars, ShellRect monitor)
    {
        var best = 0;
        long most = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            if (bars[i] == monitor)
            {
                return i;
            }

            var shared = Shared(bars[i], monitor);
            if (shared > most)
            {
                most = shared;
                best = i;
            }
        }

        return best;
    }

    private static long Shared(ShellRect one, ShellRect other)
    {
        var width = Math.Min((long)one.X + one.Width, (long)other.X + other.Width) - Math.Max(one.X, other.X);
        var height = Math.Min((long)one.Y + one.Height, (long)other.Y + other.Height) - Math.Max(one.Y, other.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    /// <summary>
    /// The Bar <paramref name="id"/>'s Panel opens on (<see cref="PanelSession.BarFor"/>): the Bar that was clicked, else where a Panel
    /// is already open, else the monitor with the keyboard. The Menu is a Panel here, so it opens at the middle of that Bar's monitor.
    /// </summary>
    /// <param name="id">The plugin.</param>
    /// <param name="clicked">The Bar whose item's gesture asked for the Panel, or null.</param>
    private int BarOf(string id, int? clicked)
    {
        if (clicked is null && _clicked is { } view && _views.IndexOf(view) is >= 0 and var at)
        {
            clicked = at;
        }

        var bar = _session.BarFor(id, clicked, KeyboardBar());
        return bar >= 0 && bar < _views.Count ? bar : 0;
    }

    /// <summary>The pill plugins draw with <see cref="IPluginContext.ShowPill"/>.</summary>
    public void UsePill(PluginPill pill) => _pill = pill;

    /// <summary>The toasts plugins show with <see cref="IPluginContext.ShowToast"/>.</summary>
    public void UseToasts(NotificationToasts toasts) => _toasts = toasts;

    /// <summary>Sends a toast's command to <paramref name="id"/>'s panel handler. A plugin that stopped is skipped. Call on the UI thread.</summary>
    /// <param name="id">The plugin that showed the toast.</param>
    /// <param name="command">The command the toast sent.</param>
    public void SendToast(string id, string command)
    {
        if (string.Equals(id, Notice.Host, StringComparison.Ordinal) && _recordingAct?.Invoke(command) == true)
        {
            return;
        }

        _runtime.SendToast(id, command);
    }

    /// <summary>
    /// Shows a Notice of the Host's own: a toast with no notification behind it, so the bell does not count it and do not disturb does not hold it back.
    /// Called from any thread.
    /// </summary>
    /// <param name="title">The first line, in bold.</param>
    /// <param name="body">The line under it.</param>
    public void ShowNotice(string title, string body) => _runtime.ShowHostNotice(Notice.Brief(title, body));

    /// <summary>Shows a Notice of the Host's own, buttons and all. Called from any thread.</summary>
    /// <param name="notice">The toast.</param>
    public void ShowRecordingNotice(ToastFace notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _runtime.ShowHostNotice(notice);
    }

    /// <summary>A button on the recording Notice, or a click on it. True when the command opened the file.</summary>
    /// <param name="act">Handles the Notice's command. False leaves it to the runtime, which hides the Notice on close.</param>
    public void UseRecording(Func<string, bool> act)
    {
        ArgumentNullException.ThrowIfNull(act);
        _recordingAct = act;
    }

    /// <summary>Hears whether any plugin holds the Escape claim. Called on the thread that changed it, at once.</summary>
    public void WatchEscape(Action<bool> changed) => _runtime.WatchEscape(changed);

    /// <summary>
    /// The modes the Host itself keeps an Indicator for (<see cref="HostIndicator"/>), and what a click on one runs.
    /// A new mode is a getter here, a line in <see cref="PublishHost"/>, and its Indicator in <see cref="HostIndicator"/>.
    /// </summary>
    /// <param name="run">Runs a Chord command by its id, as a click on one of these Indicators asks.</param>
    /// <param name="idleLock">True while Idle lock is on.</param>
    /// <param name="recording">True while Winoma records the screen. Asked on every tick, so it has to be cheap.</param>
    public void UseHost(Action<string> run, Func<bool> idleLock, Func<bool> recording)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(idleLock);
        ArgumentNullException.ThrowIfNull(recording);
        _runtime.UseHost(run);
        _idleLock = idleLock;
        _recording = recording;
    }

    /// <summary>
    /// Publishes the Indicators the Host keeps, for the modes that changed since the last call. Every tick calls it, so a setting
    /// changed in the file or in Setup shows by the next one. Call it after a Chord changed a mode, so the cluster follows at once.
    /// </summary>
    public void PublishHost()
    {
        if (_idleLock is { } idleLock && idleLock() is var enabled && _idleLockShown != enabled)
        {
            _idleLockShown = enabled;
            _runtime.SetHostIndicator(HostIndicator.IdleLock, HostIndicator.OfIdleLock(enabled));
        }

        // A recording starting or stopping calls this at once, and every tick calls it too, so the cluster follows either way.
        if (_recording is { } recording && recording() is var running && _recordingShown != running)
        {
            // The first reading is the desktop starting, not a recording that ended.
            if (_recordingShown is not null || running)
            {
                Log.Information("Capture", running ? "a recording is running" : "the recording has ended");
            }

            _recordingShown = running;
            _runtime.SetHostIndicator(HostIndicator.Recording, HostIndicator.OfRecording(running));
        }
    }

    /// <summary>Hears that a plugin changed something on screen, so the bar loop can pick up its pace.</summary>
    public void WatchActivity(Action activity) => _activity = activity;

    /// <summary>Sets or clears an indicator the extension keeps. The owner is a <c>host:</c> name.</summary>
    /// <param name="owner">A <c>host:</c> name.</param>
    /// <param name="indicator">The glyph, or null to remove it.</param>
    public void SetIndicator(string owner, PluginIndicator? indicator) => _runtime.SetHostIndicator(owner, indicator);

    /// <summary>What each running plugin published for <c>winoma desktop</c>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> Facts() => _runtime.Facts();

    /// <summary>One report per Bar, in the order the Bars were made. Each lists its items from the left, or from the top on a side bar.</summary>
    /// <returns>The bars <c>winshell state</c> prints.</returns>
    public IReadOnlyList<ShellBarReport> BarReports()
    {
        var edge = ShellEdge.Name(_edge);
        var bars = new List<ShellBarReport>(_views.Count);
        for (var i = 0; i < _views.Count; i++)
        {
            bars.Add(new ShellBarReport(i, edge, _views[i].PluginIds()));
        }

        return bars;
    }

    /// <summary>
    /// Sends <paramref name="command"/> to its plugin, or to the enabled clone that stands in for it.
    /// A plugin that is not running, or that does not declare the command, is skipped with a warning. Call on the UI thread.
    /// </summary>
    public void RunCommand(string plugin, string name, CommandPhase phase) =>
        _runtime.RunCommand(plugin, name, phase);

    /// <summary>A physical Escape the hook kept from the focused window. The plugin holding the claim hears it. Call on the UI thread.</summary>
    public void TakeEscape() => _runtime.TakeEscape();

    /// <summary>Disposes the plugins that stay loaded across a remount. Call once, at shutdown.</summary>
    public void Close() => _runtime.Close();

    /// <summary>Ends a plugin that stays loaded, so the mount that follows loads its new build. Call on the UI thread.</summary>
    public void Retire(string id) => _runtime.Retire(id);

    /// <summary>Hears which panel opened or closed. Null means none is open.</summary>
    public void WatchPanel(Action<string?> changed) => _onPanel = changed;

    /// <summary>The panel reads for <paramref name="id"/>, or the built-in reads when this host did not start it.</summary>
    /// <param name="id">The open panel's plugin id, or null.</param>
    /// <returns>The reads that should run while that panel is open.</returns>
    public PanelReads ReadsOf(string? id) => _runtime.ReadsOf(id);

    /// <summary>Hears an empty-bar drag that picked an edge, and an item dragged to another place.</summary>
    /// <param name="edge">The edge the drag selected.</param>
    /// <param name="itemMoved">The id an item is shown under, and where it was dropped. Runs on the turn after the release.</param>
    public void WatchGestures(Action<BarEdge> edge, Action<string, BarDropTarget> itemMoved)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(itemMoved);
        _edgeChosen = edge;
        _itemMoved = itemMoved;
    }

    /// <summary>Hears a double click on empty Bar. It runs what <c>winoma bar transparent toggle</c> runs.</summary>
    /// <param name="toggle">Runs on the Bar's thread.</param>
    public void WatchTransparent(Action toggle)
    {
        ArgumentNullException.ThrowIfNull(toggle);
        _transparentToggled = toggle;
    }

    private void MarkOpen(string? id)
    {
        foreach (var view in _views)
        {
            view.MarkOpen(id);
        }

        // A Panel that opened or closed gave the Bar the keyboard or took it away.
        Attend();
        _onPanel?.Invoke(id);
    }

    /// <summary>
    /// Builds every Bar from <paramref name="layout"/> and starts each plugin once. Each Bar gets its own copy of every item,
    /// and a plugin's slot is the fan of its copies, so the one face it publishes is drawn on every Bar.
    /// </summary>
    /// <param name="bars">Every monitor's Bar, the first one first. At least one.</param>
    /// <param name="edge">The edge every Bar is on.</param>
    /// <param name="layout">The resolved bar.</param>
    /// <param name="catalog">Discovered manifests, by id.</param>
    /// <param name="services">Serves each family a plugin may bind.</param>
    /// <param name="create">Constructs a plugin, or returns null when it cannot.</param>
    /// <param name="vertical">True when the bar is on the left or right edge.</param>
    /// <param name="paused">True while plugins should not be polled. Null is never paused.</param>
    /// <param name="bindTouch">Receives the host's touch callback, so a read off the UI thread can make a plugin due.</param>
    /// <param name="menuId">The plugin whose panel is the Menu, opened at the middle of the monitor. Null leaves that to a plugin whose kind is <c>menu</c>.</param>
    public void Mount(IReadOnlyList<BarWindow> bars, BarEdge edge, ResolvedLayout layout, IReadOnlyDictionary<string, PluginManifest> catalog, IPluginServiceSource services, Func<PluginManifest, IWinomaPlugin?> create, bool vertical, Func<bool>? paused = null, Action<Action<string>>? bindTouch = null, string? menuId = null)
    {
        _queue = bars[0].DispatcherQueue;
        _edge = edge;
        _paused = paused;

        // A background read that lands with a new value makes the plugin that shows it due, from the read's own thread.
        bindTouch?.Invoke(_runtime.TouchLater);

        // A Bar's window is the same one from mount to mount, so its view is kept. The view of a Bar that is gone is let go with its window.
        var views = new List<BarView>(bars.Count);
        foreach (var bar in bars)
        {
            views.Add(_views.Find(view => ReferenceEquals(view.Owner, bar)) ?? new BarView(this, bar));
        }

        _views = views;
        for (var i = 0; i < views.Count; i++)
        {
            views[i].Index = i;
        }

        if (_shown is not null && !views.Contains(_shown))
        {
            _shown = null;
        }

        // A pointer that was on a Bar still is, and its next move is still checked. No new item has heard it yet.
        _hoverArmed = views.Exists(view => view.PointerOnBar);
        if (_session.OpenId is not null)
        {
            _session.Close();
        }

        _session.ClearClicks();
        _bare.Clear();
        _commands.Clear();

        // Each Bar builds its own elements. Nothing built for one Bar is handed to another.
        var order = StartOrder(layout);
        var made = new List<List<SlotView>>(views.Count);
        foreach (var view in views)
        {
            made.Add(view.Build(edge, vertical, layout, catalog));
        }

        var starts = new List<PluginStart>();
        for (var i = 0; i < order.Count; i++)
        {
            var slot = order[i];
            var copies = new List<SlotView>(views.Count);
            foreach (var built in made)
            {
                copies.Add(built[i]);
            }

            if (slot.IsCommand)
            {
                // One program is run for the item, and what it prints is drawn on every Bar.
                _commands.Add(new CommandSlot(slot, copies));
                foreach (var copy in copies)
                {
                    copy.Bind((gesture, tag) =>
                    {
                        if (gesture == WidgetGesture.Left && slot.OnClick.Length > 0)
                        {
                            StartAsync(slot.OnClick).Forget("Plugin", "click command " + slot.Id);
                        }
                    });
                }

                continue;
            }

            if (catalog.TryGetValue(slot.Id, out var manifest))
            {
                var middle = AtMiddle(manifest, menuId);
                var surfaces = new List<IPluginSlot>(views.Count);
                for (var bar = 0; bar < views.Count; bar++)
                {
                    surfaces.Add(new SlotSurface(this, views[bar], copies[bar], middle));
                }

                starts.Add(new PluginStart(manifest, slot, new SlotFan(surfaces, BarOf)));
            }
        }

        var background = new List<PluginManifest>();
        foreach (var manifest in layout.Background)
        {
            // A panel with no bar item opens at the bar's center. The Menu opens at the middle of the screen, with its item or without it.
            if (PanelRoster.AtCenter(manifest) || AtMiddle(manifest, menuId))
            {
                starts.Add(new PluginStart(manifest, new BarSlot { Id = manifest.Id }, new PanelSurface(this, AtMiddle(manifest, menuId))));
                continue;
            }

            background.Add(manifest);
        }

        _runtime.Mount(starts, background, vertical, services, create);
        _roster.Mount(layout, catalog.Values);
    }

    /// <summary>True when the panel opens at the middle of the monitor: the Menu kind, or the id the extension named.</summary>
    private static bool AtMiddle(PluginManifest manifest, string? menuId) =>
        PanelRoster.AtMiddle(manifest)
        || (menuId is not null && string.Equals(manifest.Id, menuId, StringComparison.Ordinal));

    /// <summary>
    /// Every item of <paramref name="layout"/> in the order its plugin starts, which is the order a Bar builds its slots in:
    /// the middle group, then the left, then the right.
    /// </summary>
    internal static List<BarSlot> StartOrder(ResolvedLayout layout)
    {
        var order = new List<BarSlot>(layout.CenterBefore);
        if (!layout.CenterAsGroup)
        {
            if (layout.Anchor is not null)
            {
                order.Add(layout.Anchor);
            }

            order.AddRange(layout.CenterAfter);
        }

        order.AddRange(layout.Left);
        order.AddRange(layout.Right);
        return order;
    }

    internal void Enqueue(Action work)
    {
        if (_queue is not { } queue || !queue.TryEnqueue(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Log.Error("Plugin", "plugin update failed", ex);
            }
        }))
        {
            Log.Warning("Plugin", "a plugin update was dropped before the bar was up");
        }
    }

    /// <summary>Refreshes each due plugin and due script widget.</summary>
    public void Tick() => Tick(all: false);

    /// <summary>Refreshes each due plugin while the bar is hidden. A script widget draws only on the bar, so its program is not run.</summary>
    public void TickUnseen() => Tick(all: false, scripts: false);

    /// <summary>Refreshes every plugin, due or not, and each due script widget.</summary>
    public void TickAll() => Tick(all: true);

    private void Tick(bool all, bool scripts = true)
    {
        PublishHost();
        foreach (var view in _views)
        {
            view.CheckPointer();
        }

        // Every plugin that shows a time is stale once Windows changes zone, so all of them tick on that turn.
        if (_zone.Check(Environment.TickCount64))
        {
            Log.Information("Bar", "Windows changed time zone");
            all = true;
        }

        if (all)
        {
            _runtime.TickAll();
        }
        else
        {
            _runtime.Tick();
        }

        // An open Kit panel and every item a plugin draws itself read their getters after the plugin ticked, and change only what moved.
        _panel.RefreshKit();
        RefreshItems();
        // A script widget draws only on the bar, so its program does not run while the session is locked or the display is off.
        if (!scripts || _paused?.Invoke() == true)
        {
            return;
        }

        // The tick clock, not the local time. A command slot only needs elapsed time, and DateTimeOffset.Now reads the time zone.
        var now = Environment.TickCount64;
        foreach (var command in _commands)
        {
            if (now < command.Next)
            {
                continue;
            }

            command.Next = now + ((command.Slot.Interval <= 0 ? 5 : command.Slot.Interval) * 1000L);
            RunCommandAsync(command).Forget("Plugin", "command " + command.Slot.Id);
        }
    }

    /// <summary>Ticks the plugins that show other plugins' glyphs, and reads what they drew. One glyph changing does not tick the whole bar.</summary>
    private void TickIndicators()
    {
        _runtime.TickIndicators();
        _panel.RefreshKit();
        RefreshItems();
    }

    /// <summary>Repaints the workspace numbers only, on every Bar. A chord uses this so the highlight is not waiting on the other widgets.</summary>
    public void TickWorkspace()
    {
        // A clone that stands in for the workspaces item runs under its own id.
        var id = _runtime.Route("winoma.workspaces") ?? "winoma.workspaces";
        _runtime.Tick(id);
        foreach (var view in _views)
        {
            view.RefreshItem(id);
        }
    }

    /// <summary>Paints the open panel for the current Windows theme.</summary>
    public void ApplyChrome() => _panel.ApplyChrome();

    /// <summary>
    /// Opens a plugin's panel as if its item was left-clicked, as a <c>panel:</c> chord asks. The open panel closes instead.
    /// A hidden item, a plugin that is not running, and one that declares no panel do nothing.
    /// </summary>
    public void Open(string id) => _roster.Toggle(id);

    /// <summary>Left-clicks <paramref name="id"/>'s bar item, as the menu's CLI word does.</summary>
    /// <param name="id">Plugin id.</param>
    /// <returns>False when that plugin is not running.</returns>
    public bool Click(string id)
    {
        if (!_runtime.IsRunning(id))
        {
            return false;
        }

        _runtime.Click(id);
        return true;
    }

    /// <summary>
    /// Opens <paramref name="id"/>'s panel, or leaves it open when it already is, then sends its command, as <c>winoma summon</c> asks.
    /// A switch to another panel closes the one open. Call on the UI thread.
    /// </summary>
    /// <param name="id">The plugin whose panel opens.</param>
    /// <param name="command">The panel command to send. Empty sends none.</param>
    /// <param name="value">The number a slider or switch sends. Null sends none.</param>
    /// <param name="text">What a text field submits. Null when the command is not a field's.</param>
    /// <param name="key">An arrow key or Return the panel should press. Null sends the command as text.</param>
    /// <returns>Null when the panel is open, or the sentence saying why not.</returns>
    public string? Summon(string id, string command = "", double? value = null, string? text = null, PanelKey? key = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        // An item whose plugin draws no panel, such as the indicator cluster, has nothing to open. It hears the Panel command as a click on it sends it.
        if (command.Length > 0 && _bare.Contains(id) && _runtime.IsRunning(id))
        {
            _ = text is { } typed
                ? _runtime.SendText(id, command, typed)
                : _runtime.SendPanel(id, command, value);
            _runtime.Tick(id);
            RefreshItems();
            return null;
        }

        if (_roster.Summon(id) is { } refused)
        {
            return refused;
        }

        // "key --text Down" is the keyboard's Down: the plugin hears it, and then the Kit's selection. A key neither uses,
        // and one with a modifier such as the Menu's Ctrl+K, goes on to the plugin as text.
        if (key is { } pressed && _panel.PressKey(pressed))
        {
            return null;
        }

        if (command.Length > 0 && !_panel.PressKit(command, value, text))
        {
            _ = text is { } typed
                ? _runtime.SendText(id, command, typed)
                : _runtime.SendPanel(id, command, value);

            // A command with no control, such as a key sent to the Menu, changed the plugin all the same.
            // The plugin ticks and the panel reads its getters before the reply is taken, as after a press on a control.
            _runtime.Tick(id);
            _panel.RefreshKit();
            RefreshItems();
        }

        return null;
    }

    /// <summary>
    /// Sends <paramref name="id"/> a panel command and opens nothing first, so the plugin decides whether its panel opens or closes,
    /// as <c>winoma menu toggle</c> asks of the Menu. Call on the UI thread.
    /// </summary>
    /// <param name="id">The plugin.</param>
    /// <param name="command">The panel command.</param>
    /// <returns>False when that plugin is not running.</returns>
    public bool SendPanel(string id, string command)
    {
        if (!_runtime.SendPanel(id, command, null))
        {
            return false;
        }

        _runtime.Tick(id);
        RefreshItems();
        return true;
    }

    /// <summary>The panel on screen and what it shows, or null when none is open.</summary>
    internal SurfacePanel? Panel => _session.OpenId is { } id
        ? new SurfacePanel(id, _panel.KitOutline, _panel.KitSelected, _session.Bar < _views.Count ? _views[_session.Bar].Owner.Screen : null)
        : null;

    /// <summary>
    /// Runs a Kit control's handler for <paramref name="id"/>. A fault is logged, not thrown.
    /// The plugin then ticks and the panel reads its getters, so the press shows at once.
    /// </summary>
    private void PressKit(string id, string command, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", id + " panel command " + command + " failed", ex);
        }

        // The plugin ticks now, so the facts it publishes follow the press and not the next turn.
        _runtime.Tick(id);
        _panel.RefreshKit();
        RefreshItems();
    }

    /// <summary>
    /// Shows <paramref name="plugin"/>'s Kit panel under <paramref name="anchor"/> on <paramref name="view"/>'s Bar, or at the middle of the screen
    /// when <paramref name="middle"/> is set. A plugin that draws none is logged, and nothing opens.
    /// </summary>
    private void ShowPanel(IWinomaPlugin plugin, BarView view, FrameworkElement anchor, bool middle)
    {
        if (plugin is not IKitPanel kit)
        {
            Log.Warning("Plugin", plugin.Id + " opened a panel but does not draw one with the Kit");
            return;
        }

        // A Panel gives the Bar the keyboard, which brings the idle Indicators up and widens the cluster. That can move this item,
        // so they come up and the bar is laid out before the Panel is placed against it. A Panel placed at a point of its own needs no layout.
        if (!middle && _runtime.AttendBar(Attention(keyboard: true)))
        {
            RefreshItems();
            if (_at is null)
            {
                (view.Owner.Content as UIElement)?.UpdateLayout();
            }
        }

        // The Panel is one window, on one monitor at a time. It follows the layer of the Bar it is on.
        if (_views.Count > 1 && !(_session.Live && string.Equals(_session.OpenId, plugin.Id, StringComparison.Ordinal)))
        {
            Log.Information("Panel", plugin.Id + " opens on the monitor at " + view.Owner.Screen.X + "," + view.Owner.Screen.Y + (middle ? ", at its middle" : ""));
        }

        _shown = view;
        LayerPanel(view.Owner.PanelAbove);
        try
        {
            _panel.ShowKit(
                plugin.Id,
                view.Owner,
                view.Index,
                _edge,
                anchor,
                kit,
                (command, action) => PressKit(plugin.Id, command, action),
                (command, value) => _runtime.SendPanel(plugin.Id, command, value),
                middle,
                _at);
        }
        finally
        {
            // What the session says is open decides from here on.
            Attend();
        }
    }

    /// <summary>Reads the getters of every bar item a plugin draws itself, on every Bar.</summary>
    private void RefreshItems()
    {
        foreach (var view in _views)
        {
            view.RefreshItems();
        }
    }

    /// <summary>Opens the numbered panel widget on the right, or closes it when that panel is already open.</summary>
    /// <param name="number">1-based index among the visible panel widgets. The tray is skipped.</param>
    public void OpenRight(int number) => _roster.ToggleRight(number);

    /// <summary>
    /// Opens <paramref name="id"/>'s panel at the middle of the bar it was last shown on, wherever its item is. The bar has just moved,
    /// so its items are not laid out yet, and the bar window's own place is all that is known.
    /// </summary>
    /// <param name="id">The plugin whose panel was open before the move.</param>
    public void OpenAtCenter(string id)
    {
        if (_views.Count == 0)
        {
            return;
        }

        var view = _shown ?? _views[0];
        var hwnd = view.Owner.Hwnd;
        if (hwnd == 0 || !PointerProbe.TryWindow(hwnd, out var rect))
        {
            return;
        }

        var scale = ScreenMetrics.Scale(hwnd);
        _at = new Rect(0, 0, Math.Max(0, rect.Right - rect.Left) / scale, Math.Max(0, rect.Bottom - rect.Top) / scale);
        _clicked = view;
        try
        {
            _roster.Toggle(id);
        }
        finally
        {
            _at = null;
            _clicked = null;
        }
    }

    /// <summary>
    /// A right click on <paramref name="view"/>'s Bar itself, outside every item. The bar's own panel opens with its corner at the pointer, on that Bar.
    /// </summary>
    /// <param name="view">The Bar that was clicked.</param>
    /// <param name="at">The pointer, in that Bar's window.</param>
    /// <param name="press">What the press of this click did to the panel that was up.</param>
    /// <returns>True when the bar's panel is open after the click.</returns>
    internal bool ClickBar(BarView view, Rect at, BarPress press)
    {
        _at = at;
        _clicked = view;
        try
        {
            return _roster.ClickBar(press);
        }
        finally
        {
            _at = null;
            _clicked = null;
        }
    }

    /// <summary>A drag on a Bar itself chose <paramref name="edge"/>. Every Bar moves there.</summary>
    internal void ChooseEdge(BarEdge edge) => _edgeChosen?.Invoke(edge);

    /// <summary>An item was dragged to <paramref name="target"/> on one Bar. The one layout is written, and every Bar is built again from it.</summary>
    internal void MoveItem(string id, BarDropTarget target) => _itemMoved?.Invoke(id, target);

    /// <summary>A double click on empty Bar. The same command as <c>winoma bar transparent toggle</c>.</summary>
    internal void ToggleTransparent() => _transparentToggled?.Invoke();

    /// <summary>A pinned tray icon was let go at a point on the screen. Over a cell of the open overflow, it is placed there.</summary>
    internal void DropTray(string id, string key, int x, int y)
    {
        if (_panel.TryTrayCell(x, y, out var cell))
        {
            _runtime.SendPanel(id, TrayCommand.Place(cell, key), null);
        }
    }

    /// <summary>Closes the open panel.</summary>
    public void Dismiss() => _session.Close();

    /// <summary>How a panel asks Windows for the keyboard, as when the Menu opens.</summary>
    /// <param name="focus">Gives a window the keyboard. True when it has it.</param>
    public void UseFocus(Func<nint, bool> focus) => _panel.UseFocus(focus);

    /// <summary>How the Menu asks Windows for the keyboard as it opens, again and again until it has it.</summary>
    /// <param name="seek">Gives a window the keyboard and keeps no record of a refusal. True when it has it.</param>
    public void UseSeek(Func<nint, bool> seek) => _panel.UseSeek(seek);

    /// <summary>
    /// Keeps a panel under <paramref name="bar"/> above other windows, or lets a fullscreen window on that Bar's monitor cover it.
    /// The open panel follows at once when it is on that Bar, and a panel that opens there later starts from it.
    /// </summary>
    /// <param name="bar">The Bar whose layer was looked at.</param>
    /// <param name="above">False while a fullscreen window on its monitor may cover a panel.</param>
    public void KeepAbove(BarWindow bar, bool above)
    {
        bar.PanelAbove = above;

        // The Bar the Panel was last on, or the first before any Panel opened: with one monitor that is always this Bar.
        var on = _shown ?? (_views.Count > 0 ? _views[0] : null);
        if (on is null || ReferenceEquals(on.Owner, bar))
        {
            _panelAbove = above;
            _panel.KeepAbove(above);
        }
    }

    /// <summary>Tells the Panel window its layer when it moves to a Bar whose layer differs from the one it was last on.</summary>
    private void LayerPanel(bool above)
    {
        if (above != _panelAbove)
        {
            _panelAbove = above;
            _panel.KeepAbove(above);
        }
    }

    private async Task RunCommandAsync(CommandSlot command)
    {
        try
        {
            // A program that passed the limit has said nothing new. The item keeps the face it has, so its neighbours stay where they are.
            if (await RunAsync(command.Slot.Exec) is not { } output)
            {
                return;
            }

            var (text, tooltip) = CommandOutput.Read(output);
            var face = new BarFace { Text = text, Tooltip = tooltip, Visible = text.Length > 0 };
            foreach (var view in command.Views)
            {
                view.Apply(face);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Plugin", "command " + command.Slot.Id + " failed", ex);
        }
    }

    // Runs off the bar thread. Starting a program takes milliseconds, and ending a slow one's tree lists every process on the Host.
    private static Task<string?> RunAsync(string exec) => Task.Run(() => RunProgramAsync(exec));

    /// <summary>What the program printed, or null when it had not finished printing after two seconds.</summary>
    private static async Task<string?> RunProgramAsync(string exec)
    {
        var (file, args) = CommandOutput.Split(exec);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();

        // The bytes are read, not the text: the process's own reader decodes in a code page the program may not print in.
        var output = process.StandardOutput.BaseStream;
        var buffer = new byte[CommandOutput.MaxBytes];
        var count = 0;
        var late = false;

        // The limit is the read's own token. A slow program's read ends there, with nothing left pending to fault later,
        // and the timer is gone once the program has answered.
        using var limit = new CancellationTokenSource(2000);
        try
        {
            // A full buffer ends the read too, so a program that prints without end is stopped there and not at the limit.
            while (count < buffer.Length)
            {
                var read = await output.ReadAsync(buffer.AsMemory(count), limit.Token);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }
        }
        catch (OperationCanceledException)
        {
            late = true;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        return late ? null : CommandOutput.Decode(buffer.AsSpan(0, count), ConsolePage.Encoding);
    }

    // A clicked program is the person's own: it is not waited for, not stopped, and what it prints is not read.
    // Started off the bar thread, as a run is.
    private static Task StartAsync(string exec) => Task.Run(() =>
    {
        var (file, args) = CommandOutput.Split(exec);
        using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
    });

    /// <summary>True while the pointer is on a Bar, so a pointer move should run <see cref="PointerMoved"/>. Safe from any thread.</summary>
    public bool HoverArmed => _hoverArmed;

    /// <summary>
    /// The pointer moved while it was on a Bar. An item whose pointer left its Bar without crossing its edge lets its hover go,
    /// and that Bar hears that the pointer is gone. A still pointer cannot leave a Bar, so this runs on a move and not on a timer.
    /// The bar's tick checks as well.
    /// </summary>
    public void PointerMoved()
    {
        var watched = false;
        foreach (var view in _views)
        {
            watched |= view.PointerMoved();
        }

        if (!watched)
        {
            _hoverArmed = false;
        }
    }

    internal void ArmHover() => _hoverArmed = true;

    internal void DisarmHover()
    {
        foreach (var view in _views)
        {
            if (view.Watched)
            {
                return;
            }
        }

        _hoverArmed = false;
    }

    /// <summary>
    /// Tells the indicator cluster where the Bars' pointer and keyboard are. It is ticked when either moved, and its drawing on every Bar
    /// reads what it decided, so the idle Indicators come and go on all Bars together.
    /// </summary>
    internal void Attend()
    {
        if (_runtime.AttendBar(Attention(_roster.BarHasKeyboard)))
        {
            RefreshItems();
        }
    }

    /// <summary>The one attention every Bar shares: the pointer is on the Bar while it is on any of them.</summary>
    private BarAttention Attention(bool keyboard)
    {
        return BarAttention.Across(_views.ConvertAll(view => view.PointerOnBar), keyboard);
    }

    private sealed class CommandSlot(BarSlot slot, IReadOnlyList<SlotView> views)
    {
        public BarSlot Slot { get; } = slot;

        /// <summary>The item's slot on each Bar.</summary>
        public IReadOnlyList<SlotView> Views { get; } = views;
        public long Next { get; set; } = long.MinValue;
    }

    /// <summary>The panel, the pill, and toasts, as the runtime reaches them.</summary>
    private sealed class HostSurface(BarHost host) : IPluginSurface
    {
        // A panel with a close noted is already leaving. A refresh then would count as a new open: the panel would
        // slide in again and swallow the outside click that dismissed it.
        public bool PanelOpen(string id) => host._session.Live && string.Equals(host._session.OpenId, id, StringComparison.Ordinal);

        public void DismissPanel() => host._session.Close();

        public void HoldDismiss(bool hold) => host._session.SuspendDismiss(hold);

        public void After(Action work) => host._session.Post(work);

        public void TakeKeyboard() => host._session.TakeKeyboard();

        public void CopyText(string text)
        {
            if (text.Length > 0 && !ShellClipboard.Text(text))
            {
                Log.Warning("Bar", "the clipboard did not take a plugin's copy");
            }
        }

        public void OpenPath(string path)
        {
            if (path.Length == 0)
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
            {
                Log.Warning("Bar", "Windows did not open " + path + ": " + ex.Message);
            }
        }

        public void ShowPill(string id, PillFace face) => host.Enqueue(() =>
        {
            host._pill?.Show(id, face);

            // A plugin draws the pill when its state changes, often from a thread of its own, so its bar item is due now and need not ask often.
            host._runtime.Touch(id);
            host._activity?.Invoke();
        });

        public void FeedPill(string id, float level) => host._pill?.Feed(id, level);

        public void ShowToast(string id, ToastFace toast) => host.Enqueue(() =>
        {
            host._toasts?.Show(id, toast);
            host._activity?.Invoke();
        });

        public void HideToasts(string id, string? key) => host.Enqueue(() => host._toasts?.Hide(id, key));

        public void IndicatorsChanged() => host.Enqueue(() =>
        {
            host.TickIndicators();
            host._activity?.Invoke();
        });

        public void Forget(string? escapeOwner, Func<string, bool> running)
        {
            if (escapeOwner is not null)
            {
                host._pill?.Show(escapeOwner, PillFace.Hidden);
            }

            // A plugin can show the pill without holding Escape, so the pill's own owner is asked about as well.
            host._pill?.Forget(running);
            host._toasts?.Forget(id => running(id));
        }
    }

    /// <summary>
    /// A panel plugin with no bar item. Its panel opens at the center of the Bar that was clicked. When no click asked for it,
    /// that is the Bar a panel is already open on, or else the one on the monitor with the keyboard (<see cref="BarOf"/>).
    /// The Menu opens at the middle of that Bar's monitor.
    /// </summary>
    private sealed class PanelSurface(BarHost host, bool middle) : IPluginSlot
    {
        public void SetFace(BarFace face)
        {
        }

        public void OpenPanel(IWinomaPlugin plugin)
        {
            var view = host._views[host.BarOf(plugin.Id, clicked: null)];
            host.ShowPanel(plugin, view, view.Anchor, middle);
        }

        public void Bind(IWinomaPlugin input, IWinomaPlugin plugin)
        {
        }
    }

    /// <summary>
    /// A plugin's slot on one Bar and the panel under it. The Menu's panel opens at the middle of the screen instead.
    /// A plugin has one of these for each Bar, behind a <see cref="SlotFan"/>.
    /// </summary>
    private sealed class SlotSurface(BarHost host, BarView bar, SlotView view, bool middle) : IPluginSlot
    {
        public void SetFace(BarFace face) => view.Apply(face);

        public void OpenPanel(IWinomaPlugin plugin) => host.ShowPanel(plugin, bar, view.PanelAnchor, middle);

        public void Bind(IWinomaPlugin input, IWinomaPlugin plugin)
        {
            view.Bind((gesture, tag) => input.OnGesture(gesture, tag));
            if (plugin is not IKitPanel)
            {
                host._bare.Add(plugin.Id);
            }

            // A plugin that draws its own item builds it here, once for this Bar, with a Kit of this Bar's own.
            if (plugin is IKitBar item)
            {
                view.UseKit(item, (command, action) => host.PressKit(plugin.Id, command, action), () => host._monitorAt?.Invoke(bar.Owner.Screen) ?? 0);
            }
        }
    }
}

/// <summary>The panel on screen, for the shell's report.</summary>
/// <param name="Id">The plugin whose panel it is.</param>
/// <param name="Rows">The panel's Kit controls as text. Null before it has drawn.</param>
/// <param name="Selected">The command of the control the arrow keys are on. Null when they are on none.</param>
/// <param name="Screen">The monitor the panel is on, whole, in physical pixels. Null when it is not known.</param>
internal sealed record SurfacePanel(string Id, IReadOnlyList<PanelLine>? Rows, string? Selected, ShellRect? Screen);

