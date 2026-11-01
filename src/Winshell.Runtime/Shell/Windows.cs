using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>ShellWindow</c>: the base of PanelWindow, FloatingWindow and PopupWindow. Its items are drawn by
/// a <see cref="Scene"/> into a native window the platform provides.
/// </summary>
[QmlDefaultProperty("data")]
[QmlSignal("closed")]
[QmlSignal("resourcesLost")]
[QmlSignal("windowConnected")]
public abstract class ShellWindow : Reloadable, IWindowClient, IGlobalPosition
{
    private bool _active;
    private double _implicitWidth = 100;
    private double _implicitHeight = 100;
    private double _width = 100;
    private double _height = 100;
    private bool _visible = true;
    private QColor _color = QColor.White;
    private Region? _mask;
    private ShellScreen? _screen;
    private string? _shownOnScreenId;
    private bool _updatesEnabled = true;
    private double _devicePixelRatio = 1;
    private IWindowBackend? _backend;
    private SurfaceRestarts? _restarts;
    private IDisposable? _restartTimer;
    private bool _rebuildOnShow;
    private readonly QObject _windowTransform = new();
    private WindowBackdrop _backdrop;
    private WindowCorners _corners;
    private bool _resourcesLost;

    protected ShellWindow()
    {
        SurfaceFormat = new SurfaceFormatGroup(this);
        Scene = new Scene(this);
        Scene.Root.SetLayoutWidth(_width);
        Scene.Root.SetLayoutHeight(_height);
        Scene.UpdateRequested += () => _backend?.RequestFrame();
        Scene.CursorChanged += c => _backend?.SetCursor(c);
        Scene.FocusChanged += OnSceneFocus;
        Theme.Changed += OnThemeChanged;
    }

    public Scene Scene { get; }

    public IWindowBackend? Backend => _backend;

    protected abstract WindowKind Kind { get; }

    [Qml]
    public Item ContentItem => Scene.Root;

    [Qml]
    public ItemData Data => Scene.Root.Data;

    [Qml]
    public double Width
    {
        get { Track(); return _width; }
        set => ImplicitWidth = value;
    }

    [Qml]
    public double Height
    {
        get { Track(); return _height; }
        set => ImplicitHeight = value;
    }

    /// <summary>True while the native window is the foreground window.</summary>
    [Qml] public bool Active { get { Track(); return _active; } }

    [Qml]
    public double ImplicitWidth
    {
        get { Track(); return _implicitWidth; }
        set
        {
            if (SetField(ref _implicitWidth, Math.Max(1, value)))
                OnRequestedSizeChanged();
        }
    }

    [Qml]
    public double ImplicitHeight
    {
        get { Track(); return _implicitHeight; }
        set
        {
            if (SetField(ref _implicitHeight, Math.Max(1, value)))
                OnRequestedSizeChanged();
        }
    }

    [Qml]
    public virtual bool Visible
    {
        get { Track(); return _visible; }
        set
        {
            if (SetField(ref _visible, value))
                UpdateVisibility();
        }
    }

    [Qml]
    public bool BackingWindowVisible
    {
        get { Track(); return _backend?.IsVisible ?? false; }
    }

    [Qml]
    public QColor Color
    {
        get { Track(); return _color; }
        set
        {
            var wasOpaque = ColorIsOpaque;
            if (SetField(ref _color, value))
            {
                ApplyBackdrop();
                Scene.RequestUpdate();
                SurfaceFormat.NoteColor(wasOpaque);
            }
        }
    }

    /// <summary>True when <see cref="Color"/> has no transparency. An invalid color is not opaque.</summary>
    internal bool ColorIsOpaque => _color.A8 == 255;

    /// <summary>The <c>surfaceFormat</c> group. The group itself can't be replaced.</summary>
    [Qml]
    public SurfaceFormatGroup SurfaceFormat { get; }

    /// <inheritdoc />
    public bool? SurfaceOpaque => SurfaceFormat.Requested;

    [Qml]
    public Region? Mask
    {
        get { Track(); return _mask; }
        set
        {
            if (_mask == value)
                return;
            _maskConnection?.Dispose();
            _mask = value;
            _maskConnection = value?.Connect("changed", _ => UpdateMask());
            NotifyChanged("mask");
            UpdateMask();
        }
    }

    private IDisposable? _maskConnection;

    [Qml]
    public virtual ShellScreen? Screen
    {
        get { Track(); return _screen ?? ShownOn; }
        set
        {
            if (SetField(ref _screen, value))
                OnPlacementChanged();
        }
    }

    /// <summary>The screen the window is on now, which a dragged window or an unplugged screen can make differ from <see cref="Screen"/>.</summary>
    internal ShellScreen? ShownOn
    {
        get
        {
            var screens = Engine?.GetService<ScreenRegistry>();
            return (_shownOnScreenId is { } id ? screens?.Find(id) : null) ?? _screen ?? screens?.Primary;
        }
    }

    [Qml]
    public double DevicePixelRatio
    {
        get { Track(); return _devicePixelRatio; }
    }

    [Qml]
    public bool UpdatesEnabled
    {
        get { Track(); return _updatesEnabled; }
        set
        {
            if (SetField(ref _updatesEnabled, value) && value)
                Scene.RequestUpdate();
        }
    }

    [Qml]
    public QObject WindowTransform
    {
        get { Track(); return _windowTransform; }
    }

    /// <summary>
    /// The Windows 11 material behind the window: none, Mica or Acrylic. A contrast theme reports none, and the
    /// window clears to the contrast window color wherever its own color is transparent.
    /// </summary>
    public WindowBackdrop Backdrop
    {
        get => Theme.Current.IsHighContrast ? WindowBackdrop.None : _backdrop;
        set
        {
            _backdrop = value;
            ApplyBackdrop();
        }
    }

    public WindowCorners Corners
    {
        get => _corners;
        set
        {
            _corners = value;
            ApplyBackdrop();
        }
    }

    private void OnThemeChanged()
    {
        if (IsDestroyed)
            return;
        ApplyBackdrop();
        Scene.RequestUpdate();
    }

    private void ApplyBackdrop()
    {
        var backdrop = Theme.Current.IsHighContrast ? WindowBackdrop.None : _backdrop;
        Scene.TextOnBackdrop = backdrop != WindowBackdrop.None || PaintedColor().A < 1;
        _backend?.SetBackdrop(backdrop, _corners, Theme.Current.IsDark);
    }

    /// <summary>The color the surface clears to. Contrast replaces a transparent material with the system window color.</summary>
    private QColor PaintedColor()
    {
        var color = _color.IsValid ? _color : QColor.White;
        if (Theme.Current.IsHighContrast && _backdrop != WindowBackdrop.None && color.A < 1)
            return Theme.Current.SolidBackgroundFillColorBase;
        return color;
    }

    public QPoint GlobalOrigin => _backend?.Origin ?? new QPoint(0, 0);

    [Qml]
    public QPoint ItemPosition(Item item) => (QPoint)Scene.Root.MapFromItem(item, 0, 0);

    [Qml]
    public QRect ItemRect(Item item) => (QRect)Scene.Root.MapFromItem(item, 0, 0, item.Width, item.Height);

    [Qml]
    public object MapFromItem(Item item, object? xOrPoint, double y = double.NaN, double width = double.NaN, double height = double.NaN)
    {
        var value = ValueConverter.Unwrap(xOrPoint);
        return value switch
        {
            QPoint p => Scene.Root.MapFromItem(item, p.X, p.Y),
            QRect r => Scene.Root.MapFromItem(item, r.X, r.Y, r.Width, r.Height),
            _ => Scene.Root.MapFromItem(item, ValueConverter.ToDouble(value, "real"), y, width, height),
        };
    }

    // Lifetime

    protected override void OnReload(QObject? oldInstance)
    {
        var old = oldInstance as ShellWindow;
        // A native window that cannot be made or placed fails like one that throws later: it is reported and made
        // again after a backoff, and the shell's other windows still open.
        try
        {
            // Take over the old shell's native window when it is the same kind, so a reload does not flicker.
            if (old is not null && old.Kind == Kind && old._backend is not null && Engine?.GetService<IShellPlatform>() is not null)
            {
                _backend = old.DetachBackend(this);
            }
            else
            {
                CreateBackend();
            }
            OnBackendCreated();
            Emit("windowConnected");
        }
        catch (SurfaceResourcesException e)
        {
            OnResourcesLost(e);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            OnFailed(e);
        }
        ReloadChildrenRecursive(this, oldInstance);
        foreach (var child in Scene.Root.ChildItems)
            ReloadRecursive(child, old?.Scene.Root);
    }

    private IWindowBackend? DetachBackend(IWindowClient newClient)
    {
        var backend = _backend;
        _backend = null;
        if (backend is IRebindable rebindable)
            rebindable.Rebind(newClient);
        return backend;
    }

    private void CreateBackend()
    {
        var platform = Engine?.GetService<IShellPlatform>();
        if (platform is null)
            return;
        _backend = platform.CreateWindow(Kind, this);
    }

    /// <summary>Sends placement and appearance to a new backend, then shows it.</summary>
    protected virtual void OnBackendCreated()
    {
        ApplyBackdrop();
        OnPlacementChanged();
        UpdateMask();
        UpdateVisibility();
        Scene.RequestUpdate();
        _backend?.RequestFrame();
    }

    protected virtual void OnRequestedSizeChanged() => OnPlacementChanged();

    /// <summary>Sends the window's placement to the platform. Each kind of window has its own.</summary>
    protected abstract void OnPlacementChanged();

    protected virtual bool ShouldShow => _visible;

    protected void UpdateVisibility()
    {
        if (_backend is null)
        {
            // The owner closed and Windows destroyed this window. Showing it again makes a new one.
            if (_rebuildOnShow && !_resourcesLost && ShouldShow && _restartTimer is null && !IsDestroyed)
            {
                _rebuildOnShow = false;
                try
                {
                    CreateBackend();
                    OnBackendCreated();
                }
                catch (SurfaceResourcesException e)
                {
                    OnResourcesLost(e);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    OnFailed(e);
                }
            }
            return;
        }
        var show = ShouldShow;
        if (_backend.IsVisible != show)
        {
            if (show)
                OnPlacementChanged();
            _backend.SetVisible(show);
            NotifyChanged("backingWindowVisible");
        }
        if (show)
            Scene.RequestUpdate();
        WindowRegistry.Changed(this);
    }

    private void UpdateMask()
    {
        if (_backend is null)
            return;
        if (_mask is null)
        {
            _backend.SetInputRegion(null);
            return;
        }
        using var region = _mask.ToSkRegion(Scene.Root);
        var rects = new List<QRect>();
        if (_mask.Intersection != 0)
        {
            // Not Combine: the mask applies on top of the whole window.
            using var full = new SKRegion(SKRectI.Create(0, 0, (int)Math.Ceiling(_width), (int)Math.Ceiling(_height)));
            var op = (Intersection)_mask.Intersection switch
            {
                Intersection.Subtract => SKRegionOperation.Difference,
                Intersection.Intersect => SKRegionOperation.Intersect,
                _ => SKRegionOperation.XOR,
            };
            full.Op(region, op);
            Collect(full, rects);
        }
        else
        {
            Collect(region, rects);
        }
        _backend.SetInputRegion(rects);

        static void Collect(SKRegion region, List<QRect> into)
        {
            using var iterator = region.CreateRectIterator();
            while (iterator.Next(out var r))
                into.Add(new QRect(r.Left, r.Top, r.Width, r.Height));
        }
    }

    /// <summary>The item in this window that has keyboard focus, or null.</summary>
    [Qml]
    public Item? ActiveFocusItem
    {
        get { Track(); return Scene.FocusItem; }
    }

    private void OnSceneFocus(Item? item)
    {
        NotifyChanged("activeFocusItem");
        if (item is not null && WantsKeyboardFocus())
            _backend?.SetKeyboardFocus(true);
    }

    /// <summary>Whether an item that took focus may pull keyboard focus to this window.</summary>
    protected virtual bool WantsKeyboardFocus() => true;

    // IWindowClient

    public void OnResized(double width, double height, double devicePixelRatio)
    {
        if (IsDestroyed)
            return;
        if (SetField(ref _devicePixelRatio, devicePixelRatio, nameof(DevicePixelRatio)))
            Scene.DevicePixelRatio = devicePixelRatio;
        var changed = false;
        if (_width != width)
        {
            _width = width;
            NotifyChanged("width");
            changed = true;
        }
        if (_height != height)
        {
            _height = height;
            NotifyChanged("height");
            changed = true;
        }
        Scene.Root.SetLayoutWidth(width);
        Scene.Root.SetLayoutHeight(height);
        if (changed)
        {
            NotifyChanged("windowTransform");
            UpdateMask();
        }
        Scene.RequestUpdate();
    }

    public void OnMoved(double x, double y) => NotifyChanged("windowTransform");

    public void OnPaint(SKCanvas canvas)
    {
        if (IsDestroyed)
            return;
        canvas.Clear(PremultipliedBackground());
        if (!_updatesEnabled)
            return;
        var context = Scene.Paint(canvas, Engine?.Loop.Now ?? TimeSpan.Zero);
        if (context.WantsAnotherFrame)
            Scene.RequestUpdate();
    }

    /// <summary>The window color as the surface clears to. With a backdrop the window shows through.</summary>
    private SKColor PremultipliedBackground() => PaintedColor().ToSk();

    public void OnClosed()
    {
        if (IsDestroyed)
            return;
        Emit("closed");
        if (_visible)
        {
            _visible = false;
            NotifyChanged("visible");
        }
        NotifyChanged("backingWindowVisible");
        WindowRegistry.Changed(this);
    }

    /// <summary>The native window became the foreground window, or stopped being it.</summary>
    public void OnActiveChanged(bool active)
    {
        if (IsDestroyed || _active == active)
            return;
        _active = active;
        NotifyChanged("active");
    }

    public void OnScreenChanged(string screenId)
    {
        if (IsDestroyed || _shownOnScreenId == screenId)
            return;
        _shownOnScreenId = screenId;
        if (_screen is null)
            NotifyChanged("screen");
    }

    public virtual void OnDismissed()
    {
    }

    public virtual void OnStateChanged(bool maximized, bool minimized)
    {
    }

    public virtual void OnFullscreenChanged(bool fullscreen)
    {
    }

    /// <summary>Windows destroyed the native window. The shell window stays, hidden, until it is shown again.</summary>
    public void OnBackingDestroyed()
    {
        if (IsDestroyed || _resourcesLost)
            return;
        _backend = null;
        _rebuildOnShow = true;
        OnClosed();
    }

    /// <summary>
    /// The native window failed: report it, close it, and make another after a backoff, so one broken window does
    /// not take the shell down or stay broken. A window that keeps failing stays closed until the next reload.
    /// </summary>
    public void OnFailed(Exception error)
    {
        if (IsDestroyed || Engine is not { } engine)
            return;
        var where = SurfaceName;
        var delay = (_restarts ??= new SurfaceRestarts()).Fail(engine.Loop.Now);
        var recovery = delay is { } next
            ? $"Winshell opens it again in {next.TotalSeconds:0} second{(next.TotalSeconds == 1 ? "" : "s")}."
            : "It failed several times in a few minutes, so Winshell leaves it closed until the configuration reloads.";
        var report = engine.GetService<CrashReports>()?.Write(where, error, subject: Subject, recovery: recovery);
        _backend?.Dispose();
        _backend = null;
        NotifyChanged("backingWindowVisible");
        WindowRegistry.Changed(this);
        if (delay is { } wait)
            engine.Log.Error("window", $"{where} failed and opens again in {wait.TotalSeconds:0} s: {error.GetType().Name}: {error.Message}");
        else
            engine.Log.Error("window", $"{where} failed {SurfaceRestarts.MaxFailures} times in {SurfaceRestarts.Period.TotalMinutes:0} minutes and stays closed until the configuration reloads: {error.GetType().Name}: {error.Message}");
        engine.Log.Error("window", error.ToString());
        if (report is not null)
            engine.Log.Info("window", $"Wrote a crash report to \"{report.Path}\"");
        if (delay is not null)
            _restartTimer = engine.Loop.Schedule(delay.Value, Restart);
    }

    public void OnDrawFailed(Exception error)
    {
        if (IsDestroyed || Engine is not { } engine)
            return;
        if (engine.GetService<CrashReports>()?.WriteOnce(SurfaceName + " drawing", error, subject: "the panel", recovery: "The panel could not draw. The rest of Winshell is still running.") is { } report)
            engine.Log.Info("window", $"Wrote a crash report to \"{report.Path}\"");
    }

    /// <summary>
    /// The surface is gone and will not be made again. <c>resourcesLost</c> is sent first, then the window closes.
    /// A second call does nothing, so a device that stays lost cannot open windows in a loop.
    /// </summary>
    public void OnResourcesLost(Exception error)
    {
        if (IsDestroyed || _resourcesLost)
            return;
        _resourcesLost = true;
        Engine?.Log.Warn("window", $"The window lost the resources it draws with and closes: {error.Message}");
        Emit("resourcesLost");
        _restartTimer?.Dispose();
        _restartTimer = null;
        _rebuildOnShow = false;
        var backend = _backend;
        _backend = null;
        OnClosed();
        try
        {
            backend?.Dispose();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Engine?.Log.Warn("window", $"Could not release the window after resourcesLost: {e.GetType().Name}: {e.Message}");
        }
        NotifyChanged("backingWindowVisible");
    }

    private void Restart()
    {
        _restartTimer = null;
        if (IsDestroyed || _backend is not null || _resourcesLost)
            return;
        try
        {
            CreateBackend();
            OnBackendCreated();
            Emit("windowConnected");
        }
        catch (SurfaceResourcesException e)
        {
            OnResourcesLost(e);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            OnFailed(e);
        }
    }

    /// <summary>
    /// The window as a sentence names it for a person: a floating window's title, or the file it is declared in, so
    /// <c>Taskbar.qml</c> is "the taskbar". A window declared in <c>shell.qml</c> is "a window".
    /// </summary>
    internal string Subject
    {
        get
        {
            if (this is FloatingWindow { Title.Length: > 0 } floating)
                return floating.Title;
            var name = CreationContext?.Imports?.Document.File is { Length: > 0 } file ? Path.GetFileNameWithoutExtension(file) : "";
            if (name.Length == 0 || name.Equals("shell", StringComparison.OrdinalIgnoreCase))
                return "a window";
            var words = new System.Text.StringBuilder("the ");
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    words.Append(' ');
                words.Append(char.ToLowerInvariant(name[i]));
            }
            return words.ToString();
        }
    }

    /// <summary>The window as a crash report names it: its type, the file it is declared in, and its screen.</summary>
    internal string SurfaceName
    {
        get
        {
            var name = TypeName;
            if (ObjectName.Length > 0)
                name += $" \"{ObjectName}\"";
            if (CreationContext?.Imports?.Document.File is { Length: > 0 } file)
                name += $" in {Path.GetFileName(file)}";
            if (ShownOn is { } screen)
                name += $" on {screen.Name}";
            return name;
        }
    }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnThemeChanged;
        _restartTimer?.Dispose();
        _maskConnection?.Dispose();
        WindowRegistry.Remove(this);
        Scene.Root.Destroy();
        _backend?.Dispose();
        _backend = null;
        base.OnDestroy();
    }
}

/// <summary>
/// When a failed window opens again: after 1, 2, 4, 8 and 16 seconds. The sixth failure within five minutes
/// leaves it closed.
/// </summary>
internal sealed class SurfaceRestarts
{
    public const int MaxFailures = 6;

    public static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

    private readonly Queue<TimeSpan> _failures = new();

    /// <summary>Counts a failure at <paramref name="now"/>. Returns how long to wait before opening again, or null to stay closed.</summary>
    public TimeSpan? Fail(TimeSpan now)
    {
        while (_failures.TryPeek(out var first) && now - first >= Period)
            _failures.Dequeue();
        _failures.Enqueue(now);
        if (_failures.Count >= MaxFailures)
            return null;
        return TimeSpan.FromSeconds(1 << (_failures.Count - 1));
    }
}

/// <summary>A backend that can be handed to the window of a new shell after a reload.</summary>
public interface IRebindable
{
    void Rebind(IWindowClient client);
}

/// <summary>Tracks the shell's visible windows, for <c>Shell.lastWindowClosed</c>.</summary>
public static class WindowRegistry
{
    // One set per shell: its settings, which outlive its generations, or its engine when it has none. Winshell's own
    // notice runs in an engine of its own, so it never keeps a shell's last window from closing.
    [ThreadStatic]
    private static Dictionary<object, HashSet<ShellWindow>>? _windows;

    internal static void Changed(ShellWindow window)
    {
        if (window.Engine is not { } engine)
            return;
        var settings = engine.GetService<ShellSettings>();
        _windows ??= [];
        var key = (object?)settings ?? engine;
        if (!_windows.TryGetValue(key, out var windows))
            _windows[key] = windows = [];
        var wasAny = windows.Count > 0;
        if (window.BackingWindowVisible)
            windows.Add(window);
        else
            windows.Remove(window);
        if (windows.Count == 0)
            _windows.Remove(key);
        if (!wasAny || windows.Count != 0)
            return;
        if (settings is not null)
            settings.Emit("lastWindowClosed");
        else if (engine.FindNativeSingleton<ShellGlobal>() is { } shell)
            shell.Emit("lastWindowClosed");
    }

    internal static void Remove(ShellWindow window)
    {
        if (_windows is null)
            return;
        foreach (var (key, windows) in _windows.ToList())
        {
            if (windows.Remove(window) && windows.Count == 0)
                _windows.Remove(key);
        }
    }
}

/// <summary>The <c>surfaceFormat</c> group of a shell window. <see cref="Opaque"/> cannot be changed after the native window exists.</summary>
public sealed class SurfaceFormatGroup(ShellWindow owner) : QObject
{
    private bool? _opaque;

    /// <summary>The value QML set, or null when it set none. The platform keeps the premultiplied chain when this is null.</summary>
    internal bool? Requested => _opaque;

    [Qml]
    public bool Opaque
    {
        get
        {
            Track();
            return _opaque ?? owner.ColorIsOpaque;
        }
        set
        {
            if (owner.Backend is not null)
            {
                if (_opaque != value)
                    owner.Engine?.Log.Warn("window", "Cannot set window surface format.");
                return;
            }
            if (_opaque == value)
                return;
            _opaque = value;
            NotifyChanged("opaque");
        }
    }

    internal void NoteColor(bool wasOpaque)
    {
        if (_opaque is null && wasOpaque != owner.ColorIsOpaque)
            NotifyChanged("opaque");
    }
}

/// <summary>The <c>anchors</c> group of a PanelWindow.</summary>
public sealed class PanelAnchors(PanelWindow owner) : QObject
{
    private bool _top, _bottom, _left, _right;

    [Qml] public bool Top { get { Track(); return _top; } set { if (SetField(ref _top, value)) owner.AnchorsChanged(); } }

    [Qml] public bool Bottom { get { Track(); return _bottom; } set { if (SetField(ref _bottom, value)) owner.AnchorsChanged(); } }

    [Qml] public bool Left { get { Track(); return _left; } set { if (SetField(ref _left, value)) owner.AnchorsChanged(); } }

    [Qml] public bool Right { get { Track(); return _right; } set { if (SetField(ref _right, value)) owner.AnchorsChanged(); } }

    public Edges Edges => (_top ? Edges.Top : 0) | (_bottom ? Edges.Bottom : 0) | (_left ? Edges.Left : 0) | (_right ? Edges.Right : 0);

    public bool HorizontalConstraint => _left && _right;

    public bool VerticalConstraint => _top && _bottom;

    /// <summary>The edge a panel reserves space at: the one anchored edge across the free axis.</summary>
    public Edges ExclusionEdge
    {
        get
        {
            var h = _left ^ _right;
            var v = _top ^ _bottom;
            if (v && !h)
                return _top ? Edges.Top : Edges.Bottom;
            if (h && !v)
                return _left ? Edges.Left : Edges.Right;
            return Edges.None;
        }
    }
}

/// <summary>The <c>margins</c> group of a PanelWindow.</summary>
public sealed class PanelMargins(PanelWindow owner) : QObject
{
    private double _top, _bottom, _left, _right;

    [Qml] public double Top { get { Track(); return _top; } set { if (SetField(ref _top, value)) owner.AnchorsChanged(); } }

    [Qml] public double Bottom { get { Track(); return _bottom; } set { if (SetField(ref _bottom, value)) owner.AnchorsChanged(); } }

    [Qml] public double Left { get { Track(); return _left; } set { if (SetField(ref _left, value)) owner.AnchorsChanged(); } }

    [Qml] public double Right { get { Track(); return _right; } set { if (SetField(ref _right, value)) owner.AnchorsChanged(); } }
}

/// <summary>The <c>PanelWindow</c>: a frameless window attached to screen edges, which can reserve space.</summary>
public sealed class PanelWindow : ShellWindow
{
    private double _exclusiveZone;
    private int _exclusionMode = 2;
    private bool _focusable;
    private bool _aboveWindows = true;
    private string _namespace = "winshell";

    public PanelWindow()
    {
        Anchors = new PanelAnchors(this);
        Margins = new PanelMargins(this);
    }

    [QmlEnum]
    public enum ExclusionModeValue
    {
        Normal = 0,
        Ignore = 1,
        Auto = 2,
    }

    protected override WindowKind Kind => WindowKind.Panel;

    [Qml]
    public PanelAnchors Anchors { get; }

    [Qml]
    public PanelMargins Margins { get; }

    [Qml]
    public double ExclusiveZone
    {
        get { Track(); return _exclusiveZone; }
        set
        {
            SetField(ref _exclusiveZone, value);
            if (_exclusionMode != 0)
            {
                _exclusionMode = 0;
                NotifyChanged("exclusionMode");
            }
            OnPlacementChanged();
        }
    }

    [Qml]
    public int ExclusionMode
    {
        get { Track(); return _exclusionMode; }
        set
        {
            if (SetField(ref _exclusionMode, value))
                OnPlacementChanged();
        }
    }

    /// <summary>
    /// Whether the panel takes the keyboard when an item in it takes focus. A panel that becomes focusable while an
    /// item in it has focus takes the keyboard then.
    /// </summary>
    [Qml]
    public bool Focusable
    {
        get { Track(); return _focusable; }
        set
        {
            if (!SetField(ref _focusable, value))
                return;
            OnPlacementChanged();
            if (value && Scene.FocusItem is not null)
                Backend?.SetKeyboardFocus(true);
        }
    }

    [Qml]
    public bool AboveWindows
    {
        get { Track(); return _aboveWindows; }
        set
        {
            if (SetField(ref _aboveWindows, value))
                OnPlacementChanged();
        }
    }

    /// <summary>What the panel is for, such as <c>bar</c>.</summary>
    public string Namespace
    {
        get => _namespace;
        set
        {
            _namespace = value;
            OnPlacementChanged();
        }
    }

    internal void AnchorsChanged() => OnPlacementChanged();

    /// <summary>The space this panel reserves at its exclusion edge, as its exclusion mode computes it.</summary>
    public double ComputedExclusiveZone
    {
        get
        {
            using var _ = DependencyTracker.Suspend();
            return _exclusionMode switch
            {
                1 => 0,
                0 => _exclusiveZone,
                _ => Anchors.ExclusionEdge switch
                {
                    Edges.Top => ImplicitHeight + Margins.Top + Margins.Bottom,
                    Edges.Bottom => ImplicitHeight + Margins.Bottom + Margins.Top,
                    Edges.Left => ImplicitWidth + Margins.Left + Margins.Right,
                    Edges.Right => ImplicitWidth + Margins.Right + Margins.Left,
                    _ => 0,
                },
            };
        }
    }

    protected override void OnPlacementChanged()
    {
        if (Backend is null)
            return;
        using var _ = DependencyTracker.Suspend();
        Backend.SetPanel(new PanelPlacement(
            Screen?.Info,
            Anchors.Edges,
            Margins.Left,
            Margins.Top,
            Margins.Right,
            Margins.Bottom,
            ImplicitWidth,
            ImplicitHeight,
            _exclusionMode,
            ComputedExclusiveZone,
            _aboveWindows,
            _focusable,
            _namespace));
    }

    protected override bool WantsKeyboardFocus() => _focusable;
}

/// <summary>The <c>FloatingWindow</c>: a normal desktop window.</summary>
public sealed class FloatingWindow : ShellWindow
{
    private string _title = "";
    private QSize _minimumSize;
    private QSize _maximumSize;
    private bool _maximized, _minimized, _fullscreen;
    private ShellWindow? _parent;
    private IDisposable? _ownerConnected;
    private IDisposable? _ownerDestroyed;
    private bool _stateRequested;

    public FloatingWindow()
    {
        Backdrop = WindowBackdrop.Mica;
    }

    protected override WindowKind Kind => WindowKind.Floating;

    [Qml]
    public string Title { get { Track(); return _title; } set { if (SetField(ref _title, value ?? "")) OnPlacementChanged(); } }

    [Qml]
    public QSize MinimumSize { get { Track(); return _minimumSize; } set { if (SetField(ref _minimumSize, value)) OnPlacementChanged(); } }

    [Qml]
    public QSize MaximumSize { get { Track(); return _maximumSize; } set { if (SetField(ref _maximumSize, value)) OnPlacementChanged(); } }

    [Qml]
    public bool Maximized
    {
        get { Track(); return _maximized; }
        set
        {
            if (!SetField(ref _maximized, value))
                return;
            // Fullscreen is borderless, so maximized is false while it is on. Setting it true leaves fullscreen.
            if (value && _fullscreen)
            {
                _fullscreen = false;
                NotifyChanged("fullscreen");
                _stateRequested = true;
            }
            OnPlacementChanged();
        }
    }

    [Qml]
    public bool Minimized { get { Track(); return _minimized; } set { if (SetField(ref _minimized, value)) OnPlacementChanged(); } }

    /// <summary>Whether the window is borderless and covering its monitor. A minimized fullscreen window stays fullscreen.</summary>
    [Qml]
    public bool Fullscreen { get { Track(); return _fullscreen; } set { if (SetField(ref _fullscreen, value)) OnPlacementChanged(); } }

    /// <summary>The owning window. Set before the native window exists, or changed later. A closing owner is left linked so Windows closes this window with it.</summary>
    [Qml]
    public ShellWindow? ParentWindow
    {
        get { Track(); return _parent is { IsDestroyed: false } ? _parent : null; }
        set
        {
            if (ReferenceEquals(_parent, value) || ReferenceEquals(value, this) || (value is not null && WouldOwn(value)))
                return;
            var previous = _parent;
            WatchOwner(value);
            _parent = value;
            NotifyChanged("parentWindow");
            if (value is null && previous is { IsDestroying: true })
                return;
            OnPlacementChanged();
        }
    }

    /// <summary>Shows the window, restores it when it is minimized, and brings it to the front with keyboard focus.</summary>
    [Qml]
    public void Activate()
    {
        Visible = true;
        Minimized = false;
        Backend?.SetKeyboardFocus(true);
    }

    [Qml]
    public void StartSystemMove() => Backend?.StartSystemMove();

    [Qml]
    public void StartSystemResize(int edges) => Backend?.StartSystemResize((Edges)edges);

    public override void OnStateChanged(bool maximized, bool minimized)
    {
        if (SetField(ref _maximized, maximized, nameof(Maximized))) { }
        if (SetField(ref _minimized, minimized, nameof(Minimized))) { }
    }

    public override void OnFullscreenChanged(bool fullscreen)
    {
        if (SetField(ref _fullscreen, fullscreen, nameof(Fullscreen))) { }
    }

    protected override void OnDestroy()
    {
        _ownerConnected?.Dispose();
        _ownerDestroyed?.Dispose();
        base.OnDestroy();
    }

    protected override void OnPlacementChanged()
    {
        if (Backend is null)
            return;
        using var _ = DependencyTracker.Suspend();
        var stateRequested = _stateRequested;
        _stateRequested = false;
        var owner = _parent is { IsDestroyed: false } ? _parent : null;
        Backend.SetFloating(new FloatingPlacement(Screen?.Info, _title, ImplicitWidth, ImplicitHeight, _minimumSize, _maximumSize, _maximized, _minimized, _fullscreen, owner, stateRequested));
    }

    private void WatchOwner(ShellWindow? owner)
    {
        _ownerConnected?.Dispose();
        _ownerDestroyed?.Dispose();
        _ownerConnected = null;
        _ownerDestroyed = null;
        if (owner is null)
            return;
        _ownerConnected = owner.Connect("windowConnected", _ =>
        {
            if (!IsDestroyed)
                OnPlacementChanged();
        });
        _ownerDestroyed = owner.Connect("destroyed", _ =>
        {
            if (IsDestroyed || !ReferenceEquals(_parent, owner))
                return;
            // Leave the native owner in place: Windows then closes this window, and the handle is dropped with it.
            _parent = null;
            _ownerConnected?.Dispose();
            _ownerConnected = null;
            NotifyChanged("parentWindow");
        });
    }

    private bool WouldOwn(ShellWindow owner)
    {
        var hop = owner;
        for (var n = 0; hop is not null && n < 16; n++)
        {
            if (ReferenceEquals(hop, this))
                return true;
            hop = hop is FloatingWindow floating ? floating._parent : null;
        }
        return false;
    }
}
