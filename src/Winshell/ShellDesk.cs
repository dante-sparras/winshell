using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>
/// The shell's desk: a bar on every monitor, the panel, the toasts, and the pill, whether or not an extension is loaded.
/// An extension hands it a look, the plugin services, and the hands. The windows are this process's.
/// </summary>
internal sealed class ShellDesk : IShellHost
{
    private readonly IReadOnlyList<IShellExtension> _extensions;
    private readonly DispatcherQueue _queue;
    private readonly BarChrome _chrome = new();
    private readonly List<BarWindow> _bars = [];
    private readonly Dictionary<string, Dictionary<string, object>> _extensionFacts = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _pipeStop = new();
    private readonly object _pipeGate = new();
    private ShellConfig _config;
    private BarHost? _host;
    private SurfaceLook _look = SurfaceLook.Shell;
    private SurfaceHands _hands = SurfaceHands.None;
    private IPluginServiceSource? _services;
    private Func<PluginManifest, IWinomaPlugin?>? _create;
    private Action<string>? _forget;
    private PluginPill? _pill;
    private NotificationToasts? _toasts;
    private WinlandClient? _winland;
    private ShellPush? _pushed;
    private Thread? _pipe;
    private readonly BarCadence _cadence = new();
    private readonly ShellTrim _trim = new();
    private DispatcherTimer? _turnTimer;
    private long _timerMs = BarCadence.FastMs;
    private int _idleSeconds;
    private NamedPipeServerStream? _listening;
    private bool _pluginEscape;
    private bool _offered;
    private bool _ready;
    private bool _shown;
    private bool _stopping;
    private bool _deskClosed;

    public ShellDesk(ShellConfig config, IReadOnlyList<IShellExtension> extensions)
    {
        _config = config;
        _extensions = extensions;
        _queue = DispatcherQueue.GetForCurrentThread();
        if (_config.HidesTaskbar)
        {
            ShellWin32.HideTaskbar();
        }
        else
        {
            ShellWin32.ShowTaskbar();
        }

        foreach (var extension in _extensions)
        {
            try
            {
                extension.Start(this);
            }
            catch (Exception ex)
            {
                Log.Error("Extension", extension.Name + " did not start", ex);
            }
        }

        _host = new BarHost();
        _pill = new PluginPill(
            (plugin, command) => _host.RunCommand(plugin, command, CommandPhase.Run),
            () => _config.BarVisible ? _config.BarEdge : null);
        _toasts = new NotificationToasts(
            () => _bars.ConvertAll(bar => new ToastBar(bar.Hwnd, bar.Covered)),
            (plugin, command) => _host.SendToast(plugin, command));
        _host.UsePill(_pill);
        _host.UseToasts(_toasts);
        WireHands();
        FollowNow();
        Mount();
        PlaceAll();
        Paint();
        _winland = WinlandClient.Start(BuildStamp.Value, message => Log.Information("Winland", message));
        _winland.Event += line => _queue.TryEnqueue(() => OnWinland(line));
        _winland.ConnectionChanged += () => _queue.TryEnqueue(OnConnected);
        _pipe = new Thread(ServePipe) { IsBackground = true, Name = "Winshell.Ctl" };
        _pipe.Start();
        // After the extension's start work, which was queued while Start ran and opens the desktop pipe.
        // ready is the line a swap waits for, so it is logged once that work has run.
        _queue.TryEnqueue(DispatcherQueuePriority.Low, StartTurning);
    }

    /// <summary>Starts the bar's turn. Queued behind the extension's desktop open, so the first ready line follows it.</summary>
    private void StartTurning()
    {
        if (_stopping || _deskClosed)
        {
            return;
        }

        // The loop is the safety net behind events. It stays fast while something is going on,
        // falls to once a second when the shell is idle, and to once in five seconds while nobody is there (ADR 0029).
        _turnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BarCadence.FastMs) };
        _turnTimer.Tick += (_, _) => Turn();
        _cadence.Activity(Environment.TickCount64);
        _turnTimer.Start();
        Turn();
    }

    public string BaseDirectory => AppContext.BaseDirectory;

    public string ConfigDirectory => ConfigFolder.Path;

    public ShellConfig Config => _config;

    public string? OpenPanelId => _host?.OpenPanelId;

    public ShellPanel? PanelView
    {
        get
        {
            var panel = _host?.Panel;
            return panel is null ? null : new ShellPanel(panel.Id, panel.Rows, panel.Selected, panel.Screen);
        }
    }

    public int BarThickness =>
        _config.BarVisible && _bars.Count > 0
            ? ScreenMetrics.Dips(_bars[0].Hwnd, BarPlacement.Thickness(_config.BarEdge))
            : 0;

    public bool HoverArmed => _host?.HoverArmed ?? false;

    public void OnUi(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_queue.HasThreadAccess)
        {
            work();
        }
        else
        {
            _queue.TryEnqueue(() => work());
        }
    }

    public string Summon(string pluginId, string? command)
    {
        Wake();
        if (TryExtension(pluginId, command))
        {
            return "ok";
        }

        var refused = _host?.Summon(pluginId, command ?? "");
        return refused ?? (_host is null ? pluginId + " is not running" : "ok");
    }

    public string? SummonFields(string pluginId, string command, double? value, string? text, PanelKey? key)
    {
        if (TryExtension(pluginId, command))
        {
            return null;
        }

        // Null from the bar means the panel opened. It is not a missing bar.
        if (_host is null)
        {
            return pluginId + " is not running";
        }

        return _host.Summon(pluginId, command, value, text, key);
    }

    public string Dismiss()
    {
        Wake();
        _host?.Dismiss();
        return "dismissed";
    }

    public void RunPlugin(string pluginId, string command, bool down)
    {
        Wake();
        _host?.RunCommand(pluginId, command, down ? CommandPhase.Down : CommandPhase.Up);
    }

    public void RunPhase(string pluginId, string command, CommandPhase phase)
    {
        Wake();
        _host?.RunCommand(pluginId, command, phase);
    }

    public void UseServices(IPluginServiceSource? source) => _services = source;

    public void UseFactory(Func<PluginManifest, IWinomaPlugin?>? create, Action<string>? forget)
    {
        _create = create;
        _forget = forget;
    }

    public void SetIndicator(string owner, PluginIndicator? indicator) => _host?.SetIndicator(owner, indicator);

    public void ShowNotice(ToastFace notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        Wake();
        _host?.ShowRecordingNotice(notice);
    }

    public void ShowBrief(string title, string body)
    {
        Wake();
        _host?.ShowNotice(title, body);
    }

    public void SetFacts(string extensionName, IReadOnlyDictionary<string, object?>? facts)
    {
        if (string.IsNullOrEmpty(extensionName) || facts is null || facts.Count == 0)
        {
            _extensionFacts.Remove(extensionName);
            return;
        }

        var kept = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, value) in facts)
        {
            if (value is bool or byte or short or int or long or float or double or string)
            {
                kept[name] = value;
            }
        }

        if (kept.Count == 0)
        {
            _extensionFacts.Remove(extensionName);
        }
        else
        {
            _extensionFacts[extensionName] = kept;
        }
    }

    public void ApplyLook(SurfaceLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        _look = look;
        _offered = true;
        if (_bars.Count > 0)
        {
            Paint();
        }
    }

    public void UseHands(SurfaceHands? hands)
    {
        _hands = hands ?? SurfaceHands.None;
        WireHands();
    }

    public bool TryDispatch(string command, IReadOnlyList<string> args, out string error)
    {
        Wake();
        if (_winland is { } client)
        {
            return client.TryDispatch(command, args, out error);
        }

        error = "Winland is not running";
        return false;
    }

    public bool TryQuery(string name, bool json, out string body)
    {
        if (_winland is { } client)
        {
            return client.TryQuery(name, json, out body);
        }

        body = "";
        return false;
    }

    public void ReloadSettings() => OnUi(ReloadNow);

    public ShellReport Snapshot() => Report();

    public void Open(string pluginId)
    {
        Wake();
        _host?.Open(pluginId);
    }

    public bool Click(string pluginId)
    {
        Wake();
        return _host?.Click(pluginId) ?? false;
    }

    public void OpenRight(int number)
    {
        Wake();
        _host?.OpenRight(number);
    }

    public bool SendPanel(string pluginId, string command)
    {
        Wake();
        return _host?.SendPanel(pluginId, command) ?? false;
    }

    public void OpenAtCenter(string pluginId)
    {
        Wake();
        _host?.OpenAtCenter(pluginId);
    }

    public PanelReads ReadsOf(string? pluginId) => _host?.ReadsOf(pluginId) ?? default;

    public void TakeEscape() => _host?.TakeEscape();

    public void Retire(string pluginId)
    {
        _forget?.Invoke(pluginId);
        _host?.Retire(pluginId);
    }

    public void PublishHost() => _host?.PublishHost();

    public void PointerMoved() => _host?.PointerMoved();

    public void TickWorkspace() => _host?.TickWorkspace();

    public void SyncBars() => OnUi(() => Look(force: true));

    public void FollowMonitors() => OnUi(MatchMonitors);

    public void PushNow() => Push();

    /// <inheritdoc />
    public void Wake()
    {
        if (_queue.HasThreadAccess)
        {
            Poke();
            return;
        }

        _queue.TryEnqueue(Poke);
    }

    /// <inheritdoc />
    public void NoteUnseen() => _trim.Unseen();

    public void CloseDesk(bool replaced)
    {
        if (_deskClosed)
        {
            return;
        }

        _deskClosed = true;
        _turnTimer?.Stop();

        // Winland is not this stop. winoma stop asks it from the desktop, and winshell stop leaves it running.
        if (!replaced)
        {
            try
            {
                ShellWin32.ShowTaskbar();
            }
            catch (Exception ex)
            {
                Log.Error("Bar", "showing the taskbar failed", ex);
            }
        }

        try
        {
            _host?.Close();
        }
        catch (Exception ex)
        {
            Log.Error("Plugin", "plugins did not stop", ex);
        }

        _pipeStop.Cancel();
        lock (_pipeGate)
        {
            try
            {
                _listening?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }

        _winland?.Dispose();
        _winland = null;
    }

    private bool TryExtension(string pluginId, string? command)
    {
        foreach (var extension in _extensions)
        {
            if (extension.TrySummon(pluginId, command))
            {
                return true;
            }
        }

        return false;
    }

    private void WireHands()
    {
        if (_host is null)
        {
            return;
        }

        _host.UseFocus(window => _hands.TryFocus(window));
        _host.UseSeek(window => _hands.TrySeek(window));
        _host.UseKeyboard(() => _hands.ScreenWithKeyboard() ?? (_bars.Count > 0 ? _bars[0].Screen : new ShellRect(0, 0, 1920, 1080)));
        _host.UseMonitors(screen => _hands.MonitorAt?.Invoke(screen) ?? 0);
        _host.WatchGestures(
            edge =>
            {
                _hands.RequestEdge(edge);
                ReloadNow();
            },
            (id, target) =>
            {
                _hands.RequestMove(id, target);
                ReloadNow();
            });
        _host.WatchTransparent(() =>
        {
            _hands.RequestTransparent();
            ReloadNow();
        });
        _host.WatchEscape(claimed =>
        {
            _pluginEscape = claimed;
            _hands.EscapeChanged?.Invoke(claimed);
            Push();
        });
        _host.WatchActivity(() => _hands.Activity?.Invoke());
        _host.WatchPanel(id =>
        {
            Wake();
            _hands.PanelChanged?.Invoke(id);
            Push();
        });
        _host.UseHost(
            chord => _hands.TryRunChord(chord),
            () => _hands.IdleLock?.Invoke() ?? false,
            () => _hands.Recording?.Invoke() ?? false);
        _host.UseRecording(command => _hands.RecordingAct?.Invoke(command) == true);
    }

    private void ReloadNow()
    {
        if (_stopping || _deskClosed)
        {
            return;
        }

        try
        {
            _config = ShellConfig.Read(Path.Combine(ConfigFolder.Path, ConfigFolder.SettingsFile));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            Log.Error("Config", "settings could not be read", ex);
            return;
        }

        if (_config.HidesTaskbar)
        {
            ShellWin32.HideTaskbar();
        }
        else
        {
            ShellWin32.ShowTaskbar();
        }

        _host?.Dismiss();
        _pill = new PluginPill(
            (plugin, command) => _host?.RunCommand(plugin, command, CommandPhase.Run),
            () => _config.BarVisible ? _config.BarEdge : null);
        _host?.UsePill(_pill);
        Mount();
        PlaceAll();
        Paint();
        Push();
    }

    private void Turn()
    {
        if (_host is null || _stopping || _deskClosed)
        {
            return;
        }

        _idleSeconds = ShellWin32.IdleSeconds();
        try
        {
            _host.PublishHost();
            if (_config.BarVisible)
            {
                _host.Tick();
            }
            else
            {
                _host.TickUnseen();
            }

            if (FollowNow())
            {
                Mount();
                PlaceAll();
                Paint();
            }
            else
            {
                Look(force: false);
            }

            Push();
            ShellWin32.Reassert();
            if (!_ready)
            {
                _ready = true;
                Log.Information("Startup", "ready pid=" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            Schedule();
            // A panel, a pending recount, or an Escape claim holds the trim back. The sweep does not wait to be away.
            _trim.Consider(OpenPanelId is not null || _hands.IsHeldFast() || _pluginEscape);
        }
    }

    /// <summary>
    /// Sets the wait before the next turn. An open panel and a pending recount keep the fast pace.
    /// A locked session, a dark display, or a minute without input lets it fall to the away pace.
    /// A plugin that holds Escape stays at the idle pace (ADR 0029).
    /// </summary>
    private void Schedule()
    {
        if (_turnTimer is not { } timer || _stopping || _deskClosed)
        {
            return;
        }

        var busy = OpenPanelId is not null || _hands.IsHeldFast();
        var away = !_pluginEscape && (_hands.IsPaused() || _idleSeconds >= BarCadence.AwayAfterSeconds);
        var wall = DateTime.UtcNow;
        var wait = _cadence.Next(Environment.TickCount64, busy, away, wall.Second, wall.Millisecond);
        if (BarCadence.Keeps(_timerMs, wait))
        {
            return;
        }

        SetTimer(timer, wait);
    }

    private void SetTimer(DispatcherTimer timer, long wait)
    {
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(wait);
        timer.Start();
        _timerMs = wait;
    }

    /// <summary>Something happened. The loop returns to the fast pace at once when it was idle or away.</summary>
    private void Poke()
    {
        if (!_cadence.Activity(Environment.TickCount64) || _timerMs <= BarCadence.FastMs || _turnTimer is not { IsEnabled: true } timer)
        {
            return;
        }

        SetTimer(timer, BarCadence.FastMs);
    }

    private void MatchMonitors()
    {
        if (!FollowNow())
        {
            return;
        }

        Mount();
        PlaceAll();
        Paint();
        Push();
    }

    private bool FollowNow()
    {
        var screens = Screens();
        if (_bars.Count == 0)
        {
            foreach (var screen in screens)
            {
                _bars.Add(MakeBar(screen));
            }

            return _bars.Count > 0;
        }

        var plan = ShellMonitors.Follow(_bars.ConvertAll(bar => bar.Screen), screens);
        if (!plan.Changed)
        {
            return false;
        }

        _host?.Dismiss();
        for (var i = _bars.Count - 1; i >= 0; i--)
        {
            if (plan.Kept[i] is { } screen)
            {
                if (_bars[i].Screen != screen)
                {
                    _bars[i].Screen = screen;
                    _bars[i].Placed = false;
                }

                continue;
            }

            var gone = _bars[i];
            _bars.RemoveAt(i);
            gone.Leaving = true;
            try
            {
                gone.Close();
            }
            catch (Exception ex)
            {
                Log.Warning("Bar", "a bar whose monitor went did not close", ex);
            }
        }

        foreach (var screen in plan.Made)
        {
            _bars.Add(MakeBar(screen));
        }

        Log.Information("Bar", "a bar on each of " + _bars.Count.ToString(CultureInfo.InvariantCulture) + " monitors");
        return true;
    }

    private static List<ShellRect> Screens()
    {
        var monitors = ShellWin32.Monitors();
        if (monitors.Count == 0)
        {
            Log.Warning("Bar", "no monitor was reported");
            return [new ShellRect(0, 0, 1920, 1080)];
        }

        return monitors.Select(item => item.Bounds).ToList();
    }

    private BarWindow MakeBar(ShellRect screen)
    {
        var bar = new BarWindow { Screen = screen };
        ShellWin32.Tag(bar.Hwnd);
        bar.Activated += (_, args) =>
        {
            if (!bar.Leaving && args.WindowActivationState != WindowActivationState.Deactivated)
            {
                Place(bar);
            }
        };
        bar.Closed += (_, _) =>
        {
            if (!_stopping && !_deskClosed && !bar.Leaving)
            {
                Stop();
            }
        };
        return bar;
    }

    private void PlaceAll()
    {
        for (var i = 0; i < _bars.Count; i++)
        {
            Place(_bars[i]);
            if (_config.BarVisible && i == 0 && !_shown)
            {
                _bars[i].Activate();
                _shown = true;
            }
        }
    }

    private void Place(BarWindow bar)
    {
        if (!bar.Presented)
        {
            BarPlacement.Present(bar.AppWindow);
            bar.Presented = true;
        }

        BarPlacement.Move(bar.AppWindow, bar.Hwnd, bar.Screen, _config.BarEdge, bar.FrameColor);
        bar.Placed = true;
        if (_config.BarVisible)
        {
            if (!bar.AppWindow.IsVisible)
            {
                bar.AppWindow.Show(false);
            }
        }
        else if (bar.AppWindow.IsVisible)
        {
            bar.AppWindow.Hide();
        }
    }

    private void Paint()
    {
        if (_bars.Count == 0)
        {
            return;
        }

        _chrome.Apply(_bars, _config.BarEdge, _offered ? _look : SurfaceLook.Shell);
        _host?.ApplyChrome();
    }

    private void Look(bool force)
    {
        // A stop closes the bars while a hover timer may still ask. The window is already gone.
        if (_stopping || _deskClosed || _hands.Yield is null || _host is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        foreach (var bar in _bars)
        {
            if (!force && !ShellLayer.LooksAgain(bar.Covered, _hands.IsCovered(bar.Screen), now - bar.SyncedAt))
            {
                continue;
            }

            var was = bar.Covered;
            var result = _hands.Yield(new BarLayerQuery(bar.Hwnd, bar.Screen, bar.Covered, bar.Behind, bar.CoveredBy));
            bar.SyncedAt = now;
            bar.Covered = result.Covered;
            bar.Behind = result.Behind;
            bar.CoveredBy = result.CoveredBy;
            bar.PanelAbove = result.PanelAbove;
            if (bar.AppWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = !result.Covered;
            }

            _host.KeepAbove(bar, result.PanelAbove);
            if (was != result.Covered)
            {
                // A window move speeds the loop only when the bar went behind a window or came back (ADR 0029).
                Wake();
                Log.Information(
                    "Bar",
                    (result.Covered ? "window covers the bar" : "bar in front")
                    + (_bars.Count > 1 ? " on monitor " + bar.Screen.X.ToString(CultureInfo.InvariantCulture) + "," + bar.Screen.Y.ToString(CultureInfo.InvariantCulture) : ""));
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The shell host is not native code. Plugins are named by manifests on disk.")]
    private void Mount()
    {
        if (_host is null || _bars.Count == 0)
        {
            return;
        }

        var discovered = PluginFolders.Discover(
            ShellPaths.PluginRoot,
            Path.Combine(ConfigFolder.Path, "plugins"));
        foreach (var warning in discovered.Warnings)
        {
            Log.Warning("Plugin", warning);
        }

        var layout = BarLayoutResolver.Resolve(_config.Bar, discovered.Plugins);
        foreach (var warning in layout.Warnings)
        {
            Log.Warning("Plugin", warning);
        }

        var catalog = discovered.Plugins.ToDictionary(plugin => plugin.Id, StringComparer.Ordinal);
        var vertical = _config.BarEdge is BarEdge.Left or BarEdge.Right;
        foreach (var bar in _bars)
        {
            bar.Pad(vertical);
        }

        _host.Mount(
            _bars,
            _config.BarEdge,
            layout,
            catalog,
            _services ?? NoServices.Instance,
            _create ?? PluginMaker.Create,
            vertical,
            () => _hands.IsPaused(),
            touch => _hands.BindTouchLater(touch),
            _hands.MenuPluginId);
    }

    private void Push()
    {
        if (_winland is null)
        {
            return;
        }

        var claim = _hands.CurrentClaim();
        var handles = new long[_bars.Count];
        for (var i = 0; i < _bars.Count; i++)
        {
            handles[i] = _bars[i].Hwnd;
        }

        var state = new ShellPush(
            claim.PickerOpen,
            claim.Escape || _pluginEscape,
            claim.SessionLocked,
            OpenPanelId is not null,
            claim.Keys,
            BarThickness,
            ShellEdge.Name(_config.BarEdge),
            _config.BarVisible ? handles : [],
            0);
        if (Same(state))
        {
            return;
        }

        _pushed = state;
        _winland.TryPush(state);
    }

    private bool Same(ShellPush state)
    {
        if (_pushed is not { } prior)
        {
            return false;
        }

        return prior.PickerOpen == state.PickerOpen
            && prior.Escape == state.Escape
            && prior.SessionLocked == state.SessionLocked
            && prior.SurfaceHeld == state.SurfaceHeld
            && prior.BarThickness == state.BarThickness
            && string.Equals(prior.BarEdge, state.BarEdge, StringComparison.Ordinal)
            && prior.Keys.SequenceEqual(state.Keys)
            && (prior.Bars ?? []).SequenceEqual(state.Bars ?? []);
    }

    private void OnConnected()
    {
        if (_winland is not { Connected: true } client)
        {
            return;
        }

        _pushed = null;
        Push();
        if (client.TryQuery("workspace", true, out var picture))
        {
            Forward("workspace", picture);
        }

        if (client.TryQuery("placed", true, out var strips))
        {
            Forward("placed", strips);
        }
    }

    private void OnWinland(string line)
    {
        if (!Wire.TryEvent(line, out var kind, out var root, out var document) || document is null)
        {
            return;
        }

        using (document)
        {
            if (kind.Equals("bind", StringComparison.Ordinal))
            {
                Wake();
                var name = JsonLine.Text(root, "name");
                var down = JsonLine.Flag(root, "down");
                var hold = JsonLine.Flag(root, "hold");
                foreach (var extension in _extensions)
                {
                    if (extension.TryHandleGlobal(name, down, hold))
                    {
                        return;
                    }
                }

                if (ShellGlobal.TryPlugin(name, out var plugin, out var command))
                {
                    if (hold)
                    {
                        _host?.RunCommand(plugin, command, down ? CommandPhase.Down : CommandPhase.Up);
                    }
                    else if (down)
                    {
                        _host?.RunCommand(plugin, command, CommandPhase.Run);
                    }

                    return;
                }

                Forward("bind", name + "\n" + (down ? "1" : "0") + (hold ? "1" : "0"));
                return;
            }

            if (kind.Equals("picker", StringComparison.Ordinal)
                && root.TryGetProperty("key", out var key)
                && key.TryGetInt32(out var id))
            {
                Forward("picker", id.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (kind.Equals("activity", StringComparison.Ordinal))
            {
                Forward("activity", "");
                return;
            }

            if (kind is "workspace" or "placed" or "focus" or "windows")
            {
                Forward(kind, JsonLine.Text(root, "body"));
            }
        }
    }

    private void Forward(string kind, string body)
    {
        foreach (var extension in _extensions)
        {
            try
            {
                extension.OnWinland(kind, body);
            }
            catch (Exception ex)
            {
                Log.Error("Extension", extension.Name + " did not take " + kind, ex);
            }
        }
    }

    private ShellReport Report()
    {
        var panel = _host?.Panel;
        IReadOnlyList<string> rows = panel?.Rows is { } lines ? lines.Select(ShellRows.Text).ToList() : [];
        return new ShellReport(panel?.Id, rows, _host?.BarReports(), _host?.Facts(), ExtensionFacts());
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> ExtensionFacts()
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.Ordinal);
        foreach (var (name, facts) in _extensionFacts)
        {
            map[name] = facts;
        }

        foreach (var extension in _extensions)
        {
            if (extension.Facts() is not { } live)
            {
                continue;
            }

            var kept = map.TryGetValue(extension.Name, out var prior)
                ? new Dictionary<string, object>(prior, StringComparer.Ordinal)
                : new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (name, value) in live)
            {
                if (value is bool or byte or short or int or long or float or double or string)
                {
                    kept[name] = value;
                }
            }

            map[extension.Name] = kept;
        }

        return map;
    }

    private void ServePipe()
    {
        while (!_pipeStop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(ShellPipe.Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                lock (_pipeGate)
                {
                    _listening = pipe;
                }

                pipe.WaitForConnectionAsync(_pipeStop.Token).GetAwaiter().GetResult();
                string? line;
                using (var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                {
                    line = reader.ReadLine();
                }

                var reply = Ask(line ?? "");
                using (var writer = new StreamWriter(pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true })
                {
                    writer.WriteLine(reply);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                if (_pipeStop.IsCancellationRequested)
                {
                    return;
                }

                Log.Warning("Pipe", "a client did not get a reply", ex);
            }
            finally
            {
                lock (_pipeGate)
                {
                    if (ReferenceEquals(_listening, pipe))
                    {
                        _listening = null;
                    }
                }

                pipe?.Dispose();
            }
        }
    }

    private string Ask(string line)
    {
        var reply = "the shell did not answer";
        using var done = new ManualResetEventSlim(false);
        if (!_queue.TryEnqueue(() =>
            {
                try
                {
                    reply = Handle(line);
                }
                catch (Exception ex)
                {
                    Log.Error("Pipe", "a client line failed", ex);
                    reply = ex.Message;
                }
                finally
                {
                    done.Set();
                }
            }))
        {
            return "the shell is stopping";
        }

        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            return "the shell did not answer";
        }

        return reply;
    }

    private string Handle(string line) =>
        ShellDispatch.Reply(
            line,
            Summon,
            Dismiss,
            (id, command, phase) => RunPhase(id, command, phase),
            Report,
            () => _queue.TryEnqueue(Stop));

    private void Stop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        if (!_deskClosed)
        {
            CloseDesk(replaced: false);
        }

        foreach (var extension in _extensions)
        {
            try
            {
                extension.Stop();
            }
            catch (Exception ex)
            {
                Log.Error("Extension", extension.Name + " did not stop", ex);
            }
        }

        LifetimeFile.Write(ShellLife.Stopped);
        ShellWin32.Release();
        foreach (var bar in _bars.ToList())
        {
            bar.Leaving = true;
            try
            {
                bar.Close();
            }
            catch (Exception ex)
            {
                Log.Warning("Bar", "a bar did not close", ex);
            }
        }

        Application.Current.Exit();
    }

    private sealed class NoServices : IPluginServiceSource
    {
        public static readonly NoServices Instance = new();

        public bool Available => false;

        public TServices? Services<TServices>()
            where TServices : class => null;
    }
}
