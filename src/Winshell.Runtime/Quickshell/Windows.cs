using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Quickshell;

/// <summary>
/// Quickshell's <c>QsWindow</c>: the base of PanelWindow, FloatingWindow and PopupWindow. Its items are drawn by
/// a <see cref="Scene"/> into a native window the platform provides.
/// </summary>
[QmlDefaultProperty("data")]
[QmlSignal("closed")]
[QmlSignal("resourcesLost")]
[QmlSignal("windowConnected")]
public abstract class QsWindow : Reloadable, IWindowClient, IGlobalPosition, IPropertyObserver
{
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
    private bool _opaqueSet;
    private bool _opaque;
    private IWindowBackend? _backend;
    private readonly QObject _windowTransform = new();
    private WindowBackdrop _backdrop;
    private WindowCorners _corners;

    protected QsWindow()
    {
        Scene = new Scene(this);
        Scene.Root.SetLayoutWidth(_width);
        Scene.Root.SetLayoutHeight(_height);
        Scene.UpdateRequested += () => _backend?.RequestFrame();
        Scene.CursorChanged += c => _backend?.SetCursor(c);
        Scene.FocusChanged += OnSceneFocus;
        SurfaceFormat = new SurfaceFormatGroup(this);
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
            if (SetField(ref _color, value))
            {
                ApplyBackdrop();
                Scene.RequestUpdate();
            }
        }
    }

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
    public SurfaceFormatGroup SurfaceFormat { get; }

    internal bool IsOpaque => _opaqueSet ? _opaque : _color.A >= 1;

    internal void SetOpaque(bool value)
    {
        _opaqueSet = true;
        _opaque = value;
    }

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

    /// <summary>The Windows 11 material behind the window: none, Mica or Acrylic.</summary>
    public WindowBackdrop Backdrop
    {
        get => _backdrop;
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
        if (!IsDestroyed)
            ApplyBackdrop();
    }

    private void ApplyBackdrop() => _backend?.SetBackdrop(_backdrop, _corners, Theme.Current.IsDark);

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

    public override void ComponentComplete()
    {
        base.ComponentComplete();
        foreach (var p in new[] { "implicitWidth", "implicitHeight" })
            Scene.Root.AddObserver(p, this);
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
    }

    protected override void OnReload(QObject? oldInstance)
    {
        var old = oldInstance as QsWindow;
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
            return;
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
            var op = _mask.Intersection switch
            {
                1 => SKRegionOperation.Difference,
                2 => SKRegionOperation.Intersect,
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

    private void OnSceneFocus(Item? item)
    {
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
    private SKColor PremultipliedBackground()
    {
        var c = _color;
        if (!c.IsValid)
            c = QColor.White;
        return c.ToSk();
    }

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

    public virtual void OnStateChanged(bool maximized, bool minimized, bool fullscreen)
    {
    }

    protected override void OnDestroy()
    {
        Theme.Changed -= OnThemeChanged;
        _maskConnection?.Dispose();
        WindowRegistry.Remove(this);
        Scene.Root.Destroy();
        _backend?.Dispose();
        _backend = null;
        base.OnDestroy();
    }
}

/// <summary>A backend that can be handed to the window of a new shell after a reload.</summary>
public interface IRebindable
{
    void Rebind(IWindowClient client);
}

/// <summary>A window's <c>surfaceFormat</c> group.</summary>
public sealed class SurfaceFormatGroup(QsWindow window) : QObject
{
    [Qml]
    public bool Opaque
    {
        get => window.IsOpaque;
        set => window.SetOpaque(value);
    }
}

/// <summary>Tracks the shell's visible windows, for <c>Quickshell.lastWindowClosed</c>.</summary>
public static class WindowRegistry
{
    [ThreadStatic]
    private static HashSet<QsWindow>? _windows;

    internal static void Changed(QsWindow window)
    {
        _windows ??= [];
        var wasAny = _windows.Count > 0;
        if (window.BackingWindowVisible)
            _windows.Add(window);
        else
            _windows.Remove(window);
        if (!wasAny || _windows.Count != 0 || window.Engine is not { } engine)
            return;
        if (engine.GetService<QuickshellSettings>() is { } settings)
            settings.Emit("lastWindowClosed");
        else if (engine.FindNativeSingleton<QuickshellGlobal>() is { } qs)
            qs.Emit("lastWindowClosed");
    }

    internal static void Remove(QsWindow window) => _windows?.Remove(window);
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

/// <summary>Quickshell's <c>PanelWindow</c>: a frameless window attached to screen edges, which can reserve space.</summary>
public sealed class PanelWindow : QsWindow
{
    private double _exclusiveZone;
    private int _exclusionMode = 2;
    private bool _focusable;
    private bool _aboveWindows = true;
    private string _namespace = "quickshell";

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

    [Qml]
    public bool Focusable
    {
        get { Track(); return _focusable; }
        set
        {
            if (SetField(ref _focusable, value))
                OnPlacementChanged();
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

    /// <summary>The layer-shell namespace, from WlrLayershell.namespace. Kept for configs that set it.</summary>
    public string Namespace
    {
        get => _namespace;
        set
        {
            _namespace = value;
            OnPlacementChanged();
        }
    }

    /// <summary>Set by WlrLayershell.layer: whether the panel sits under normal windows, as a wallpaper does.</summary>
    public int Layer { get; set; } = 2;

    internal void AnchorsChanged() => OnPlacementChanged();

    /// <summary>The space this panel reserves at its exclusion edge, as Quickshell computes it.</summary>
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
            _aboveWindows && Layer >= 2,
            _focusable,
            _namespace));
    }

    protected override bool WantsKeyboardFocus() => _focusable;
}

/// <summary>Quickshell's <c>FloatingWindow</c>: a normal desktop window.</summary>
public sealed class FloatingWindow : QsWindow
{
    private string _title = "";
    private QSize _minimumSize;
    private QSize _maximumSize;
    private bool _maximized, _minimized, _fullscreen;
    private QsWindow? _parentWindow;

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
    public bool Maximized { get { Track(); return _maximized; } set { if (SetField(ref _maximized, value)) OnPlacementChanged(); } }

    [Qml]
    public bool Minimized { get { Track(); return _minimized; } set { if (SetField(ref _minimized, value)) OnPlacementChanged(); } }

    [Qml]
    public bool Fullscreen { get { Track(); return _fullscreen; } set { if (SetField(ref _fullscreen, value)) OnPlacementChanged(); } }

    [Qml]
    public QsWindow? ParentWindow { get { Track(); return _parentWindow; } set => SetField(ref _parentWindow, value); }

    [Qml]
    public void StartSystemMove() => Backend?.StartSystemMove();

    [Qml]
    public void StartSystemResize(int edges) => Backend?.StartSystemResize((Edges)edges);

    public override void OnStateChanged(bool maximized, bool minimized, bool fullscreen)
    {
        if (SetField(ref _maximized, maximized, nameof(Maximized))) { }
        if (SetField(ref _minimized, minimized, nameof(Minimized))) { }
        if (SetField(ref _fullscreen, fullscreen, nameof(Fullscreen))) { }
    }

    protected override void OnPlacementChanged()
    {
        if (Backend is null)
            return;
        using var _ = DependencyTracker.Suspend();
        Backend.SetFloating(new FloatingPlacement(Screen?.Info, _title, ImplicitWidth, ImplicitHeight, _minimumSize, _maximumSize, _maximized, _minimized, _fullscreen));
    }
}
