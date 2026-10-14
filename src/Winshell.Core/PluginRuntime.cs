using Winshell.Core.Diagnostics;

namespace Winshell.Core;

/// <summary>
/// The plugins that are running: which ones, what each may bind, where a command goes, who holds Escape,
/// and what a stopped plugin leaves behind. A plugin marked <c>keepLoaded</c> lives through a remount, with a bar slot or without one.
/// The bar draws faces, panels, the pill, and toasts through <see cref="IPluginSurface"/>.
/// Call on the UI thread unless a member says otherwise.
/// </summary>
public sealed class PluginRuntime
{
    private readonly Dictionary<string, IWinomaPlugin> _plugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginManifest> _manifests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IWinomaPlugin> _kept = new(StringComparer.Ordinal);
    private readonly FaultRuns _faults = new();
    private readonly HashSet<string> _slotted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PanelReads> _reads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _quiet = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _due = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openAtTick = new(StringComparer.Ordinal);
    private readonly HashSet<string> _indicatorReaders = new(StringComparer.Ordinal);
    private readonly List<string> _touched = [];
    private readonly IndicatorRoster _indicators = new();
    private readonly PluginFacts _facts = new();
    private readonly RuntimeIndicators _indicatorServices;
    private BarAttention _bar;
    private readonly IPluginSurface _surface;
    private readonly Func<long> _clock;
    private readonly object _escapeGate = new();
    private string? _escapeOwner;
    private Action<bool>? _escapeChanged;
    private Action<string>? _hostPress;

    /// <summary>Creates an empty runtime that draws through <paramref name="surface"/>.</summary>
    /// <param name="surface">The bar's pill, toasts, and open panel.</param>
    public PluginRuntime(IPluginSurface surface)
        : this(surface, static () => Environment.TickCount64)
    {
    }

    /// <summary>Creates an empty runtime that draws through <paramref name="surface"/> and paces ticks by <paramref name="clock"/>.</summary>
    /// <param name="surface">The bar's pill, toasts, and open panel.</param>
    /// <param name="clock">Millisecond clock that decides when a plugin is due.</param>
    public PluginRuntime(IPluginSurface surface, Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(clock);
        _surface = surface;
        _clock = clock;
        _indicatorServices = new RuntimeIndicators(this);
    }

    /// <summary>The ids that are running, in the order they started.</summary>
    public IReadOnlyCollection<string> Running => _plugins.Keys;

    /// <summary>The glyphs plugins publish, as the indicator cluster's service family.</summary>
    public IIndicatorServices Indicators => _indicatorServices;

    /// <summary>Hears whether any plugin holds the Escape claim. Called on the thread that changed it, at once.</summary>
    /// <param name="changed">Receives true while a plugin holds the claim.</param>
    public void WatchEscape(Action<bool> changed) => _escapeChanged = changed;

    /// <summary>Hears a click on an Indicator the Host keeps (<see cref="HostIndicator"/>): its command, a Chord command id.</summary>
    /// <param name="run">Runs the Chord command.</param>
    public void UseHost(Action<string> run) => _hostPress = run;

    /// <summary>
    /// Sets or clears an Indicator the Host keeps itself, such as Idle lock switched off. It stays through a mount. Called from any thread.
    /// </summary>
    /// <param name="owner">A <c>host:</c> name, such as <see cref="HostIndicator.IdleLock"/>.</param>
    /// <param name="indicator">The glyph, or null to remove it. Its command is a Chord command id.</param>
    public void SetHostIndicator(string owner, PluginIndicator? indicator)
    {
        if (!HostIndicator.Owns(owner))
        {
            throw new ArgumentException("An Indicator the Host keeps is named host:<name>.", nameof(owner));
        }

        SetIndicator(owner, indicator);
    }

    /// <summary>
    /// Shows a Notice of the Host's own, for a Chord no plugin owns, such as the share Chord on a Host without LocalSend.
    /// It is a toast owned by <see cref="Notice.Host"/>, so a plugin that stops does not take it away. Called from any thread.
    /// </summary>
    /// <param name="notice">The toast, from <see cref="Notice.Brief"/> or <see cref="Notice.Staying"/>.</param>
    public void ShowHostNotice(ToastFace notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _surface.ShowToast(Notice.Host, notice);
    }

    /// <summary>
    /// Notes where the Bar's pointer and keyboard are, and ticks the plugins that read the indicator roster when either moved,
    /// so the cluster shows or hides its idle Indicators on this turn. Call on the UI thread.
    /// </summary>
    /// <param name="bar">Where they are now.</param>
    /// <returns>True when that is a change.</returns>
    public bool AttendBar(BarAttention bar)
    {
        if (_bar == bar)
        {
            return false;
        }

        _bar = bar;
        TickIndicators();
        return true;
    }

    /// <summary>What each running plugin published for <c>winoma desktop</c>.</summary>
    /// <returns>Facts keyed by plugin id.</returns>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> Facts() => _facts.Snapshot();

    /// <summary>True when <paramref name="id"/> is running.</summary>
    /// <param name="id">Plugin id.</param>
    /// <returns>True when it started on the last mount, or was kept through it.</returns>
    public bool IsRunning(string id) => _plugins.ContainsKey(id);

    /// <summary>False while the last face <paramref name="id"/> published asked for its bar item to be hidden.</summary>
    /// <param name="id">Plugin id.</param>
    /// <returns>True for a plugin that has published no face yet, since its item starts out shown.</returns>
    public bool Shown(string id) => !_hidden.Contains(id);

    /// <summary>The running plugin that answers for <paramref name="id"/>: itself, or the enabled clone that stands in for it.</summary>
    /// <param name="id">Plugin id, as a chord or a command names it.</param>
    /// <returns>The id to send to, or null when neither is running.</returns>
    public string? Route(string id)
    {
        if (_plugins.ContainsKey(id))
        {
            return id;
        }

        foreach (var manifest in _manifests.Values)
        {
            if (!manifest.FirstParty && manifest.ClonedFrom.Equals(id, StringComparison.Ordinal))
            {
                return manifest.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts the bar's plugins in order, then the background services. A kept service that is still enabled stays as it is.
    /// A kept plugin with a slot, or one that just lost its slot, stays loaded and hears <see cref="IWinomaPlugin.Attach"/> again.
    /// A kept plugin that is no longer enabled is disposed. Every plugin from the last mount that is not running now
    /// loses its glyph, facts, Escape claim, pill, and toasts.
    /// </summary>
    /// <param name="bar">The plugins with a slot, in the order to start them.</param>
    /// <param name="background">The services, which have no slot.</param>
    /// <param name="vertical">True when the bar is on the left or right edge.</param>
    /// <param name="services">Serves each family a plugin may bind.</param>
    /// <param name="create">Constructs a plugin, or returns null when it cannot.</param>
    public void Mount(IReadOnlyList<PluginStart> bar, IReadOnlyList<PluginManifest> background, bool vertical, IPluginServiceSource services, Func<PluginManifest, IWinomaPlugin?> create)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);
        _plugins.Clear();
        _manifests.Clear();
        _reads.Clear();
        _quiet.Clear();
        _hidden.Clear();
        _due.Clear();
        _openAtTick.Clear();
        _indicatorReaders.Clear();
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in bar)
        {
            Start(start.Manifest, start.Slot, start.View, vertical, services, create);
            Keep(start.Manifest, slotted: start.View is not null, keep);
        }

        foreach (var plugin in background)
        {
            // A kept service lives through a remount: a capture in progress, its engine, and its claims stay as they were.
            // One that had a slot on the last mount is attached again below, since that slot is gone.
            if (plugin.KeepLoaded && !_slotted.Contains(plugin.Id) && _kept.TryGetValue(plugin.Id, out var kept))
            {
                _plugins[plugin.Id] = kept;
                _manifests[plugin.Id] = plugin;
                _quiet.Add(plugin.Id);
                keep.Add(plugin.Id);
                continue;
            }

            Start(plugin, new BarSlot { Id = plugin.Id }, view: null, vertical, services, create);
            Keep(plugin, slotted: false, keep);
        }

        foreach (var id in _kept.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            Retire(id);
        }

        Forget();
        Log.Information("Plugin", "bar widgets " + string.Join(',', _plugins.Keys.OrderBy(id => id, StringComparer.Ordinal)));
    }

    /// <summary>Disposes the plugins that stay loaded across a remount. Call once, at shutdown.</summary>
    public void Close()
    {
        foreach (var id in _kept.Keys.ToList())
        {
            Retire(id);
        }

    }

    // A click reaches a plugin from inside a WinUI event, a chord, or the CLI. One that throws there is logged and the desktop goes on.
    private static void Gesture(IWinomaPlugin plugin, WidgetGesture gesture, int tag)
    {
        try
        {
            plugin.OnGesture(gesture, tag);
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", plugin.Id + " gesture " + gesture + " failed", ex);
        }
    }

    /// <summary>
    /// Ticks each plugin with a slot that is due. A plugin is due when the wait its last <see cref="IWinomaPlugin.NextTick"/> asked for has passed,
    /// when it had a gesture or command since, when it just started or its tick threw, and while its panel is open or on the first turn after it closed.
    /// A service is not ticked by a turn alone: it is ticked on the turn after <see cref="TouchLater"/> named it, and from then
    /// when the wait it asked for has passed. A service that asks for no wait is not ticked again.
    /// </summary>
    public void Tick() => Tick(all: false);

    /// <summary>Ticks every plugin with a slot, due or not. A service is ticked only when it is due, as in <see cref="Tick()"/>.</summary>
    public void TickAll() => Tick(all: true);

    /// <summary>
    /// Ticks the plugins with a slot that read the indicator roster, due or not: the indicator cluster and any plugin granted its family.
    /// The bar calls this when a plugin's glyph changed, so one glyph does not tick every plugin. <see cref="AttendBar"/> calls it too.
    /// </summary>
    public void TickIndicators()
    {
        var now = _clock();
        foreach (var plugin in _plugins.Values)
        {
            if (_indicatorReaders.Contains(plugin.Id) && !_quiet.Contains(plugin.Id))
            {
                Run(plugin, now);
            }
        }
    }

    /// <summary>Ticks <paramref name="id"/> alone, even a service. A plugin that is not running is skipped.</summary>
    /// <param name="id">Plugin id.</param>
    public void Tick(string id)
    {
        if (_plugins.TryGetValue(id, out var plugin))
        {
            Run(plugin, _clock());
        }
    }

    /// <summary>The tick after <paramref name="now"/> that a plugin asking for <paramref name="wait"/> is due at.</summary>
    /// <param name="now">Millisecond clock reading at the tick.</param>
    /// <param name="wait">What the plugin asked for. Zero or less is due on the next turn.</param>
    /// <returns>A clock reading. <see cref="long.MaxValue"/> when the wait does not fit.</returns>
    internal static long DueAt(long now, TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero)
        {
            return now;
        }

        var milliseconds = wait.TotalMilliseconds;
        return milliseconds >= long.MaxValue - (double)now ? long.MaxValue : now + (long)milliseconds;
    }

    /// <summary>Sends <paramref name="id"/> a left click, as its slot would. A plugin that is not running is skipped.</summary>
    /// <param name="id">Plugin id.</param>
    public void Click(string id)
    {
        if (_plugins.TryGetValue(id, out var plugin))
        {
            Touch(id);
            Gesture(plugin, WidgetGesture.Left, 0);
        }
    }

    /// <summary>
    /// Sends <paramref name="name"/> to <paramref name="plugin"/>, or to the enabled clone that stands in for it.
    /// A plugin that is not running, or that does not declare the command, is skipped with a warning.
    /// </summary>
    /// <param name="plugin">Plugin id.</param>
    /// <param name="name">Command name, matched ignoring case.</param>
    /// <param name="phase">A one-shot, or the edge of a held chord.</param>
    public void RunCommand(string plugin, string name, CommandPhase phase)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(name);
        var id = Route(plugin);
        if (id is null || !_plugins.TryGetValue(id, out var running))
        {
            Log.Warning("Plugin", plugin + " is not running, so " + name + " did nothing");
            return;
        }

        if (running is not ICommandPlugin target)
        {
            Log.Warning("Plugin", id + " does not take commands");
            return;
        }

        if (_manifests.TryGetValue(id, out var manifest))
        {
            var declared = manifest.Commands.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (declared is null)
            {
                Log.Warning("Plugin", id + " does not declare " + name);
                return;
            }

            name = declared.Name;
        }

        Touch(id);
        try
        {
            target.OnCommand(name, phase);
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", id + " command " + name + " failed", ex);
        }
    }

    /// <summary>
    /// Sends a toast's command to <paramref name="id"/>'s panel handler. A plugin that stopped is skipped.
    /// The close button of a Notice the Host shows (<see cref="ShowHostNotice"/>) hides that Notice.
    /// </summary>
    /// <param name="id">The plugin that showed the toast, or <see cref="Notice.Host"/>.</param>
    /// <param name="command">The command the toast sent.</param>
    public void SendToast(string id, string command)
    {
        if (string.Equals(id, Notice.Host, StringComparison.Ordinal))
        {
            if (string.Equals(command, Notice.Close, StringComparison.Ordinal))
            {
                _surface.HideToasts(Notice.Host, Notice.Key);
            }

            return;
        }

        Deliver(id, "toast command", plugin => plugin.OnPanel(command, null));
    }

    /// <summary>Sends a panel row's command to <paramref name="id"/>, as a click on that row would. <c>winoma summon</c> uses this.</summary>
    /// <param name="id">The plugin whose panel it is.</param>
    /// <param name="command">The row's command, such as <c>mode:balanced</c>.</param>
    /// <param name="value">What a slider, switch, or stepper sends with it. Null for a plain click.</param>
    /// <returns>False when that plugin is not running.</returns>
    public bool SendPanel(string id, string command, double? value) => Deliver(id, "panel command " + command, plugin => plugin.OnPanel(command, value));

    /// <summary>Submits <paramref name="text"/> to the text field whose command is <paramref name="command"/>, as Enter in that field would.</summary>
    /// <param name="id">The plugin whose panel it is.</param>
    /// <param name="command">The field's command.</param>
    /// <param name="text">What the field holds.</param>
    /// <returns>False when that plugin is not running.</returns>
    public bool SendText(string id, string command, string text) => Deliver(id, "panel text " + command, plugin => plugin.OnText(command, text));

    private bool Deliver(string id, string what, Action<IWinomaPlugin> send)
    {
        if (!_plugins.TryGetValue(id, out var plugin))
        {
            return false;
        }

        Touch(id);
        try
        {
            send(plugin);
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", id + " " + what + " failed", ex);
        }

        return true;
    }

    /// <summary>A physical Escape the hook kept from the focused window. The plugin holding the claim hears it.</summary>
    public void TakeEscape()
    {
        string? owner;
        lock (_escapeGate)
        {
            owner = _escapeOwner;
        }

        if (owner is null || !_plugins.TryGetValue(owner, out var plugin) || plugin is not IEscapePlugin escape)
        {
            return;
        }

        try
        {
            escape.OnEscape();
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", owner + " escape failed", ex);
        }
    }

    /// <summary>The panel reads for <paramref name="id"/>, or the built-in reads when this runtime did not start it.</summary>
    /// <param name="id">The open panel's plugin id, or null.</param>
    /// <returns>The reads that should run while that panel is open.</returns>
    public PanelReads ReadsOf(string? id) =>
        id is not null && _reads.TryGetValue(id, out var reads) ? reads : PanelReads.From(PluginGrants.For(id).Poll);

    private void Start(PluginManifest manifest, BarSlot slot, IPluginSlot? view, bool vertical, IPluginServiceSource services, Func<PluginManifest, IWinomaPlugin?> create)
    {
        if (_plugins.ContainsKey(manifest.Id))
        {
            return;
        }

        // A built-in that binds a family needs the extension's services. Loading it here only reports missing types.
        var family = PluginGrants.For(manifest.Id).Family;
        if (manifest.FirstParty && !manifest.IsSheet && !services.Available
            && family is not (PluginFamily.None or PluginFamily.Indicators))
        {
            Log.Information("Plugin", manifest.Id + " skipped; this shell has no services for it");
            return;
        }

        IWinomaPlugin? plugin;
        try
        {
            // A kept plugin is the one already loaded. It hears Attach again, with the context for the slot it has now.
            plugin = manifest.KeepLoaded && _kept.TryGetValue(manifest.Id, out var loaded) ? loaded : create(manifest);
            if (plugin is null)
            {
                Log.Warning("Plugin", "could not start " + manifest.Id);
                return;
            }

            if (!Grant(manifest, plugin, services))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            // A plugin that throws while it is made, or from its id or its Bind, is left out. The rest of the bar still starts.
            Log.Error("Plugin", manifest.Id + " failed to start", ex);
            return;
        }

        _reads[manifest.Id] = manifest.FirstParty
            ? PanelReads.From(PluginGrants.For(manifest.Id).Poll)
            : PluginGrants.ReadsFor(manifest);
        _plugins[plugin.Id] = plugin;
        _manifests[plugin.Id] = manifest;
        if (view is null)
        {
            _quiet.Add(plugin.Id);
        }

        var input = new SlotInput(this, plugin);
        var context = new RuntimeContext(this, input, plugin, slot, vertical, view);
        try
        {
            plugin.Attach(context);
            view?.Bind(input, plugin);
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", manifest.Id + " failed to attach", ex);
        }
    }

    /// <summary>Remembers a started <c>keepLoaded</c> plugin for the next mount, and whether it has a slot on this one.</summary>
    private void Keep(PluginManifest manifest, bool slotted, HashSet<string> keep)
    {
        if (!manifest.KeepLoaded || !_plugins.TryGetValue(manifest.Id, out var started))
        {
            return;
        }

        _kept[manifest.Id] = started;
        keep.Add(manifest.Id);
        if (slotted)
        {
            _slotted.Add(manifest.Id);
        }
        else
        {
            _slotted.Remove(manifest.Id);
        }
    }

    /// <summary>Gives a known manifest id its family. A class that reports a different id does not attach.</summary>
    private bool Grant(PluginManifest manifest, IWinomaPlugin plugin, IPluginServiceSource services)
    {
        if (!PluginGrants.MayAttach(manifest.Id, plugin.Id))
        {
            Log.Warning("Plugin", manifest.Id + " reports " + plugin.Id);
            return false;
        }

        if (!manifest.FirstParty)
        {
            var request = PluginGrants.Requested(manifest);
            foreach (var warning in request.Warnings)
            {
                Log.Warning("Plugin", warning);
            }

            foreach (var warning in PluginGrants.Unimplemented(manifest.Id, request.Grants, plugin.GetType()))
            {
                Log.Warning("Plugin", warning);
            }

            foreach (var grant in request.Grants)
            {
                Bind(grant, plugin, services, required: false);
            }

            return true;
        }

        // A sheet is the manifest. It has no class that accepts a family, so a shell with no services still draws it.
        if (manifest.IsSheet && !services.Available)
        {
            return true;
        }

        return Bind(PluginGrants.For(manifest.Id), plugin, services, required: true);
    }

    /// <summary>
    /// Binds one family onto a plugin. <see cref="PluginFamily.None"/> and <see cref="PluginFamily.Indicators"/> are bound here.
    /// The product that ships the plugins installs this once, and returns true when the plugin accepted that family's services.
    /// An unknown family returns true, as a family with nothing to bind does.
    /// </summary>
    internal static Func<PluginFamily, IWinomaPlugin, IPluginServiceSource, bool>? BindServices { get; set; }

    private bool Bind(PluginGrant grant, IWinomaPlugin plugin, IPluginServiceSource services, bool required)
    {
        if (grant.Family == PluginFamily.Indicators)
        {
            _indicatorReaders.Add(plugin.Id);
        }

        var bound = grant.Family switch
        {
            PluginFamily.None => true,
            PluginFamily.Indicators => BindTo<IIndicatorServices>(plugin, _indicatorServices),
            _ => BindServices?.Invoke(grant.Family, plugin, services) ?? false,
        };
        if (!bound && required)
        {
            Log.Warning("Plugin", plugin.Id + " does not accept its services");
            return false;
        }

        return bound || !required;
    }

    private static bool BindTo<TServices>(IWinomaPlugin plugin, TServices? services)
        where TServices : class
    {
        if (plugin is IBindable<TServices> bound && services is not null)
        {
            bound.Bind(services);
            return true;
        }

        return false;
    }

    private void Tick(bool all)
    {
        var now = _clock();
        Drain();
        foreach (var plugin in _plugins.Values)
        {
            if (_quiet.Contains(plugin.Id))
            {
                // A service has nothing to draw, so no turn ticks it by itself. It is ticked when TouchLater named it,
                // and from then at the time it asks for. One that asks for no wait is not ticked again.
                if (_due.TryGetValue(plugin.Id, out var asked) && now >= asked)
                {
                    Run(plugin, now);
                }

                continue;
            }

            // A panel that just closed gets one more tick, so the plugin forgets what it showed there.
            // A sheet keeps the wait it asked for while its panel is open. Anything else is ticked on each turn then,
            // which is how a live meter stays current.
            var open = _surface.PanelOpen(plugin.Id);
            var wasOpen = _openAtTick.Contains(plugin.Id);
            var closed = false;
            if (open)
            {
                _openAtTick.Add(plugin.Id);
            }
            else
            {
                closed = _openAtTick.Remove(plugin.Id);
            }

            var sheet = plugin is SheetPlugin;
            if (!all && !closed && !(open && !wasOpen) && (!open || sheet) && _due.TryGetValue(plugin.Id, out var due) && now < due)
            {
                continue;
            }

            Run(plugin, now);
        }
    }

    private void Run(IWinomaPlugin plugin, long now)
    {
        try
        {
            plugin.Tick();
            var wait = plugin.NextTick();
            if (wait <= TimeSpan.Zero && _quiet.Contains(plugin.Id))
            {
                // No wait means every turn for a plugin with a slot. A service that asks for none is left alone, as before it could ask.
                _due.Remove(plugin.Id);
            }
            else
            {
                _due[plugin.Id] = DueAt(now, wait);
            }

            if (_faults.Recovered(plugin.Id) is > 0 and var failed)
            {
                Log.Information("Plugin", plugin.Id + " ticks again after " + failed + " ticks failed");
            }
        }
        catch (Exception ex)
        {
            // Due again on the next turn, as every plugin was before it could ask for a wait.
            // Absent means never for a service, so one whose tick threw is named for the next turn instead.
            if (_quiet.Contains(plugin.Id))
            {
                _due[plugin.Id] = long.MinValue;
            }
            else
            {
                _due.Remove(plugin.Id);
            }

            // The retry is every turn, and the log is not: the first failure in full, then a count now and then.
            var note = _faults.Failed(plugin.Id, now);
            if (note.First)
            {
                Log.Error("Plugin", plugin.Id + " tick failed", ex);
            }
            else if (note.Count > 0)
            {
                Log.Warning("Plugin", plugin.Id + " tick has failed " + note.Count + " times in a row, last with " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Input reached <paramref name="id"/>, such as a press on its Kit panel, so it is due on the next turn.
    /// A service has heard the input already and keeps the tick it asked for.
    /// </summary>
    /// <param name="id">The plugin.</param>
    public void Touch(string id)
    {
        if (!_quiet.Contains(id))
        {
            _due.Remove(id);
        }
    }

    /// <summary>
    /// What <paramref name="id"/> shows changed off the UI thread, such as a background read that landed, so it is due on the next turn.
    /// A clone that stands in for <paramref name="id"/> is due in its place. Safe to call from any thread, and wakes nothing.
    /// This is also how a service comes to be ticked: it has no slot, so nothing else makes it due.
    /// </summary>
    /// <param name="id">The plugin, as a chord or a command names it.</param>
    public void TouchLater(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_touched)
        {
            if (!_touched.Contains(id))
            {
                _touched.Add(id);
            }
        }
    }

    private void Drain()
    {
        lock (_touched)
        {
            foreach (var id in _touched)
            {
                if (Route(id) is not { } running)
                {
                    continue;
                }

                if (_quiet.Contains(running))
                {
                    // Absent means never for a service, so the turn that drains this is told to tick it.
                    _due[running] = long.MinValue;
                }
                else
                {
                    _due.Remove(running);
                }
            }

            _touched.Clear();
        }
    }

    /// <summary>
    /// Disposes a kept plugin and drops it from the roster. A mount does this for one that is no longer enabled.
    /// The host does it for one whose assembly was built again, so the mount that follows makes the new build:
    /// a kept plugin is otherwise the same instance from mount to mount, and the new build would never run.
    /// It ends at once, also in the middle of what it was doing.
    /// </summary>
    /// <param name="id">The plugin. One that is not kept is left alone: every mount makes it again anyway.</param>
    public void Retire(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_kept.Remove(id, out var plugin))
        {
            return;
        }

        _slotted.Remove(id);

        // A manifest that stopped asking to be kept has a new instance running under this id already. That one stays.
        if (_plugins.TryGetValue(id, out var running) && ReferenceEquals(running, plugin))
        {
            _plugins.Remove(id);
        }

        try
        {
            (plugin as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning("Plugin", id + " did not dispose", ex);
        }

        Log.Information("Plugin", "stopped " + id);
    }

    /// <summary>Drops the claims, the glyph, the facts, the pill, and the toasts of plugins that are no longer running.</summary>
    private void Forget()
    {
        foreach (var item in _indicators.Snapshot())
        {
            if (!_plugins.ContainsKey(item.Plugin) && !HostIndicator.Owns(item.Plugin))
            {
                _indicators.Set(item.Plugin, null);
            }
        }

        foreach (var id in _facts.Snapshot().Keys)
        {
            if (!_plugins.ContainsKey(id))
            {
                _facts.Remove(id);
            }
        }

        string? owner;
        lock (_escapeGate)
        {
            owner = _escapeOwner;
        }

        string? stopped = null;
        if (owner is not null && !_plugins.ContainsKey(owner))
        {
            ClaimEscape(owner, claimed: false);
            stopped = owner;
        }

        _surface.Forget(stopped, id => _plugins.ContainsKey(id) || HostIndicator.Owns(id));
    }

    private void ClaimEscape(string id, bool claimed)
    {
        lock (_escapeGate)
        {
            if (claimed)
            {
                if (string.Equals(_escapeOwner, id, StringComparison.Ordinal))
                {
                    return;
                }

                _escapeOwner = id;
            }
            else if (string.Equals(_escapeOwner, id, StringComparison.Ordinal))
            {
                _escapeOwner = null;
            }
            else
            {
                return;
            }

            // Inside the gate, so two claims from different threads reach the hook in the order they were made.
            _escapeChanged?.Invoke(_escapeOwner is not null);
        }
    }

    private void SetIndicator(string id, PluginIndicator? indicator)
    {
        if (_indicators.Set(id, indicator))
        {
            _surface.IndicatorsChanged();
        }
    }

    /// <summary>The indicator roster, as the cluster's service family.</summary>
    private sealed class RuntimeIndicators(PluginRuntime runtime) : IIndicatorServices
    {
        public BarAttention Bar => runtime._bar;

        public IReadOnlyList<OwnedIndicator> Indicators => runtime._indicators.Snapshot();

        public void Press(string plugin)
        {
            if (runtime._indicators.Of(plugin) is not { Command.Length: > 0 } indicator)
            {
                return;
            }

            if (HostIndicator.Owns(plugin))
            {
                runtime._hostPress?.Invoke(indicator.Command);
            }
            else
            {
                runtime.RunCommand(plugin, indicator.Command, CommandPhase.Run);
            }
        }
    }

    /// <summary>The plugin as its slot and panel see it. A gesture, a panel row, or a field reaches the plugin and makes it due on the next turn.</summary>
    private sealed class SlotInput(PluginRuntime runtime, IWinomaPlugin plugin) : IWinomaPlugin
    {
        public string Id => plugin.Id;

        public void Attach(IPluginContext context) => plugin.Attach(context);

        public void OnGesture(WidgetGesture gesture, int tag)
        {
            runtime.Touch(plugin.Id);
            Gesture(plugin, gesture, tag);
        }

        public void OnPanel(string command, double? value)
        {
            runtime.Touch(plugin.Id);
            plugin.OnPanel(command, value);
        }

        public void OnText(string command, string text)
        {
            runtime.Touch(plugin.Id);
            plugin.OnText(command, text);
        }

        public void Tick() => plugin.Tick();

        public TimeSpan NextTick() => plugin.NextTick();
    }

    /// <summary>One plugin's context: Escape, the glyph, and facts here; the face, the panel, the pill, and toasts on the bar.</summary>
    private sealed class RuntimeContext(PluginRuntime runtime, IWinomaPlugin plugin, IWinomaPlugin owner, BarSlot slot, bool vertical, IPluginSlot? view) : IPluginContext
    {
        private BarFace? _face;

        public string Id { get; } = plugin.Id;

        public BarSlot Slot { get; } = slot;

        public bool Vertical { get; } = vertical;

        public bool PanelOpen => runtime._surface.PanelOpen(Id);

        public void SetFace(BarFace face)
        {
            ArgumentNullException.ThrowIfNull(face);
            if (face.Visible)
            {
                runtime._hidden.Remove(Id);
            }
            else
            {
                runtime._hidden.Add(Id);
            }

            // A plugin publishes on every tick, and most ticks change nothing. The same face again stops here, before the slot marks it.
            if (face.Same(_face))
            {
                return;
            }

            _face = face;
            view?.SetFace(face);
        }

        public void OpenPanel() => view?.OpenPanel(owner);

        public void DismissPanel() => runtime._surface.DismissPanel();

        public void HoldDismiss(bool hold) => runtime._surface.HoldDismiss(hold);

        public void After(Action work) => runtime._surface.After(work);

        public void TakeKeyboard() => runtime._surface.TakeKeyboard();

        public void CopyText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            runtime._surface.CopyText(text);
        }

        public void OpenPath(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            runtime._surface.OpenPath(path);
        }

        public void ClaimEscape(bool claimed) => runtime.ClaimEscape(Id, claimed);

        public void ShowPill(PillFace face)
        {
            ArgumentNullException.ThrowIfNull(face);
            runtime._surface.ShowPill(Id, face);
        }

        public void FeedPill(float level) => runtime._surface.FeedPill(Id, level);

        public void ShowToast(ToastFace toast)
        {
            ArgumentNullException.ThrowIfNull(toast);
            runtime._surface.ShowToast(Id, toast);
        }

        public void HideToasts(string? key) => runtime._surface.HideToasts(Id, key);

        public void SetIndicator(PluginIndicator? indicator) => runtime.SetIndicator(Id, indicator);

        public void SetFacts(IReadOnlyDictionary<string, object>? facts) => runtime._facts.Set(Id, facts);
    }
}
