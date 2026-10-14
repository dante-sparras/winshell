using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>One monitor's Bar, as the toasts read it.</summary>
/// <param name="Hwnd">The Bar's window, which that monitor's stack sits against.</param>
/// <param name="Covered">True while a window covers that Bar's monitor.</param>
internal readonly record struct ToastBar(nint Hwnd, bool Covered);

/// <summary>
/// The toasts plugins show with <see cref="IPluginContext.ShowToast"/>: one list of up to three, drawn on every monitor at its Bar's
/// outer corner, the newest nearest the Bar (<see cref="ToastMonitors"/>). A toast is one entry here with a window on each monitor.
/// Its time, the pointer resting on it, a click, and a dismissal belong to the entry, so it leaves every monitor together, and the
/// rest close the gap on each. A window that fills a monitor keeps new toasts off that monitor, as Windows does for a game or a video.
/// With one monitor a toast has one window, made and placed by the code that makes a second. Call every member on the UI thread.
/// </summary>
internal sealed class NotificationToasts
{
    private readonly List<Toast> _shown = [];
    private readonly Stack<ToastWindow> _spare = new();
    private readonly Func<IReadOnlyList<ToastBar>> _bars;
    private readonly Action<string, string> _send;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _trim;

    /// <summary>Creates the stack.</summary>
    /// <param name="bars">Every monitor's Bar, in the order the Bars were made. Each monitor's stack sits against its own.</param>
    /// <param name="send">Sends a toast's command to its plugin: the plugin id, then the command.</param>
    public NotificationToasts(Func<IReadOnlyList<ToastBar>> bars, Action<string, string> send)
    {
        _bars = bars;
        _send = send;
        _trim = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _trim.Interval = ToastSpares.Quiet;
        _trim.IsRepeating = false;
        _trim.Tick += (_, _) => Trim();
    }

    /// <summary>Shows <paramref name="face"/> for <paramref name="owner"/> on every monitor, or refreshes the toast with its key.</summary>
    public void Show(string owner, ToastFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        var bars = Bars();
        if (bars.Count == 0)
        {
            return;
        }

        var existing = _shown.FirstOrDefault(toast => toast.Owner == owner && toast.Key == face.Key && !toast.Leaving);
        IReadOnlyList<bool> draws = [];
        if (existing is null)
        {
            // A call or an alarm is shown over a window that covers the screen, as Windows shows its own.
            draws = ToastMonitors.Draws([.. bars.Select(bar => bar.Covered)], face.Stays);
            if (!ToastMonitors.Shown(draws))
            {
                Log.Information("Bar", "toast held back while a window covers the screen");
                Declined(owner, face);
                return;
            }

            // The new toast is not live until it is shown, so room is made before it is: the oldest that does not stay leaves.
            // With every toast on screen one that stays, such as a ringing call, the new one is left to the panel.
            var live = _shown.Where(toast => !toast.Leaving).ToList();
            if (!ToastFace.Admit([.. live.Select(toast => toast.Stays)], out var leaves))
            {
                Log.Information("Bar", "toast left to the panel while three that stay are on screen");
                Declined(owner, face);
                return;
            }

            if (leaves >= 0)
            {
                Leave(live[leaves]);
            }
        }

        var toast = existing ?? Make(owner, face.Key);
        toast.Stays = face.Stays;
        toast.Timer.Interval = face.Duration > TimeSpan.Zero ? face.Duration : ToastFace.DefaultDuration;
        if (existing is null)
        {
            for (var i = 0; i < bars.Count; i++)
            {
                if (draws[i])
                {
                    toast.Copies.Add(new Copy(bars[i].Hwnd, Take()));
                }
            }

            _shown.Insert(0, toast);
            if (bars.Count > 1)
            {
                Log.Information("Bar", "toast drawn on " + toast.Copies.Count + " of " + bars.Count + " monitors");
            }
        }

        // Each monitor's copy is measured at that monitor's scale.
        foreach (var copy in toast.Copies)
        {
            copy.Window.Fill(face, ScreenMetrics.Scale(copy.Bar), command => _send(toast.Owner, command));
        }

        MakeRoom(bars);
        Reflow(bars);

        // The time starts when the toast arrives, and again when it is refreshed. The pointer resting on any copy holds it.
        toast.Timer.Stop();
        if (!toast.Leaving && !toast.Stays && !toast.Over)
        {
            toast.Timer.Start();
        }
    }

    /// <summary>The Bars that have a window. Before the first Bar is up there is none, and nothing is shown.</summary>
    private List<ToastBar> Bars() => [.. _bars().Where(bar => bar.Hwnd != 0)];

    private Toast Make(string owner, string key)
    {
        var toast = new Toast(owner, key);
        toast.Timer.Tick += (_, _) => Leave(toast);
        return toast;
    }

    /// <summary>
    /// Sends away toasts until the rest fit beside the Bar on every monitor. A toast's height is known once it is filled, and three
    /// tall ones are more than a small screen holds. The rule for which one goes is <see cref="ToastMonitors.Overflowing"/>.
    /// </summary>
    private void MakeRoom(List<ToastBar> bars)
    {
        var live = _shown.Where(toast => !toast.Leaving).ToList();
        while (live.Count > 1)
        {
            var columns = new List<ToastColumn>(bars.Count);
            foreach (var bar in bars)
            {
                if (!Frame(bar.Hwnd, out var screen, out var frame, out var gap))
                {
                    continue;
                }

                columns.Add(new ToastColumn([.. live.Select(toast => toast.On(bar.Hwnd)?.HeightPx ?? 0)], ToastPlace.Room(screen, frame), gap));
            }

            var leaves = ToastMonitors.Overflowing(columns, [.. live.Select(toast => toast.Stays)]);
            if (leaves < 0)
            {
                return;
            }

            Log.Information("Bar", "a toast left to make room for the stack");
            Leave(live[leaves]);
            live.RemoveAt(leaves);
        }
    }

    /// <summary>Tells the owner its toast is not on screen, with the command its close button would send, so it does not count one that was never shown.</summary>
    private void Declined(string owner, ToastFace face)
    {
        if (face.Card.Dismiss.Length > 0)
        {
            _send(owner, face.Card.Dismiss);
        }
    }

    /// <summary>Slides out <paramref name="owner"/>'s toast with <paramref name="key"/>, or all of its toasts when the key is null, on every monitor.</summary>
    public void Hide(string owner, string? key)
    {
        foreach (var toast in _shown.Where(toast => toast.Owner == owner && (key is null || toast.Key == key)).ToList())
        {
            Leave(toast);
        }
    }

    /// <summary>Slides out the toasts of plugins that are no longer running.</summary>
    /// <param name="running">True for a plugin id that is still running.</param>
    public void Forget(Func<string, bool> running)
    {
        foreach (var toast in _shown.Where(toast => !running(toast.Owner)).ToList())
        {
            Leave(toast);
        }
    }

    /// <summary>Slides <paramref name="toast"/> off every monitor. Its time ran out, it was dismissed on one of them, or the stack needs its room.</summary>
    private void Leave(Toast toast)
    {
        toast.Timer.Stop();
        if (toast.Leaving)
        {
            return;
        }

        toast.Leaving = true;
        foreach (var copy in toast.Copies.ToList())
        {
            if (copy.Window.OnScreen)
            {
                copy.Window.Leave();
            }
            else
            {
                // Never placed, so it has nothing to slide out and would never report that it had gone.
                Returned(copy.Window);
            }
        }

        if (toast.Copies.Count == 0)
        {
            _shown.Remove(toast);
        }
    }

    private ToastWindow Take()
    {
        _trim.Stop();
        if (_spare.TryPop(out var spare))
        {
            return spare;
        }

        var window = new ToastWindow();
        window.Gone += Returned;
        window.Hovered += Hovered;
        return window;
    }

    /// <summary>A copy slid out and hid. Its monitor's stack closes the gap, and the toast is over once its last copy has gone.</summary>
    private void Returned(ToastWindow window)
    {
        foreach (var toast in _shown)
        {
            if (toast.Copies.RemoveAll(copy => ReferenceEquals(copy.Window, window)) > 0)
            {
                if (toast.Copies.Count == 0)
                {
                    toast.Timer.Stop();
                    _shown.Remove(toast);
                }

                break;
            }
        }

        _spare.Push(window);
        Reflow(Bars());

        // Counted from the last toast that left. A new toast stops it.
        _trim.Stop();
        _trim.Start();
    }

    /// <summary>The pointer came to rest on a copy or left it. A toast waits while the pointer is on any of its copies.</summary>
    private void Hovered(ToastWindow window)
    {
        var toast = _shown.Find(toast => toast.Copies.Exists(copy => ReferenceEquals(copy.Window, window)));
        if (toast is null || toast.Leaving)
        {
            return;
        }

        toast.Timer.Stop();
        if (!toast.Over && !toast.Stays)
        {
            toast.Timer.Start();
        }
    }

    /// <summary>The toasts were quiet for a minute. All but one spare window for each monitor close.</summary>
    private void Trim()
    {
        var close = ToastSpares.ToClose(_spare.Count, ToastSpares.Quiet, Bars().Count);
        for (var i = 0; i < close && _spare.TryPop(out var window); i++)
        {
            window.Gone -= Returned;
            window.Hovered -= Hovered;
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Warning("Bar", "a spare toast did not close", ex);
            }
        }

        if (close > 0)
        {
            Log.Debug("Bar", "closed " + close + " spare toasts");
        }
    }

    /// <summary>Stacks each monitor's copies out from its Bar. A copy whose Bar went with its monitor leaves.</summary>
    private void Reflow(List<ToastBar> bars)
    {
        foreach (var bar in bars)
        {
            if (!Frame(bar.Hwnd, out var screen, out var frame, out var gap))
            {
                continue;
            }

            var occupied = 0;
            foreach (var toast in _shown)
            {
                if (toast.On(bar.Hwnd) is not { } window)
                {
                    continue;
                }

                var slot = ToastPlace.Seat(screen, frame, window.WidthPx, window.HeightPx, occupied, gap);
                occupied += window.HeightPx + gap;
                window.Place(slot, ToastPlace.Travel(screen, slot));
            }
        }

        foreach (var toast in _shown.ToList())
        {
            foreach (var copy in toast.Copies.ToList())
            {
                if (bars.Exists(bar => bar.Hwnd == copy.Bar))
                {
                    continue;
                }

                if (copy.Window.OnScreen)
                {
                    copy.Window.Leave();
                }
                else
                {
                    toast.Copies.Remove(copy);
                    _spare.Push(copy.Window);
                }
            }

            if (toast.Copies.Count == 0)
            {
                toast.Timer.Stop();
                _shown.Remove(toast);
            }
        }
    }

    /// <summary>One Bar's monitor, its own rectangle, and the gap at that monitor's scale. False for a Bar whose window cannot be read.</summary>
    private static bool Frame(nint bar, out PixelRect screen, out PixelRect frame, out int gap)
    {
        screen = default;
        frame = default;
        gap = 0;
        if (!PointerProbe.TryWindow(bar, out var rect))
        {
            return false;
        }

        var bounds = ScreenMetrics.Of(bar);
        screen = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        frame = new PixelRect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        gap = ScreenMetrics.Dips(bar, PanelPlacement.Gap);
        return true;
    }

    /// <summary>One monitor's window of a toast.</summary>
    /// <param name="Bar">The Bar it is stacked against.</param>
    /// <param name="Window">The window on that Bar's monitor.</param>
    private readonly record struct Copy(nint Bar, ToastWindow Window);

    /// <summary>One toast: what the list holds once, however many monitors draw it.</summary>
    private sealed class Toast(string owner, string key)
    {
        /// <summary>The plugin that showed it.</summary>
        public string Owner { get; } = owner;

        /// <summary>The toast's key.</summary>
        public string Key { get; } = key;

        /// <summary>True for a toast that has no time and leaves only when it is dismissed or hidden, such as an incoming call (ADR 0057).</summary>
        public bool Stays { get; set; }

        /// <summary>True once it has been sent away. It is in the list until its last copy has slid out.</summary>
        public bool Leaving { get; set; }

        /// <summary>Its window on each monitor that draws it.</summary>
        public List<Copy> Copies { get; } = [];

        /// <summary>Its time on screen, one for all its copies.</summary>
        public DispatcherTimer Timer { get; } = new();

        /// <summary>True while the pointer rests on any copy.</summary>
        public bool Over => Copies.Exists(copy => copy.Window.Over);

        /// <summary>Its window against <paramref name="bar"/>, or null when that monitor does not draw it.</summary>
        public ToastWindow? On(nint bar)
        {
            foreach (var copy in Copies)
            {
                if (copy.Bar == bar)
                {
                    return copy.Window;
                }
            }

            return null;
        }
    }
}

