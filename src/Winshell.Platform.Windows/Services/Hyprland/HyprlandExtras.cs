using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services.Hyprland;

/// <summary>
/// Quickshell's <c>GlobalShortcut</c>. Winland has no <c>global</c> dispatcher, so the shortcut never fires; a
/// Winland bind can run <c>winshell ipc call</c> for an <c>IpcHandler</c> instead.
/// </summary>
[QmlSignal("pressed")]
[QmlSignal("released")]
public sealed class GlobalShortcut : QObject
{
    private static bool _warned;
    private string _appid = "quickshell";
    private string _name = "";
    private string _description = "";
    private string _triggerDescription = "";

    [Qml] public string Appid { get { Track(); return _appid; } set => SetField(ref _appid, value ?? ""); }

    [Qml] public string Name { get { Track(); return _name; } set => SetField(ref _name, value ?? ""); }

    [Qml] public string Description { get { Track(); return _description; } set => SetField(ref _description, value ?? ""); }

    [Qml] public string TriggerDescription { get { Track(); return _triggerDescription; } set => SetField(ref _triggerDescription, value ?? ""); }

    [Qml("pressed")] public bool IsPressed => false;

    public override void ComponentComplete()
    {
        if (_warned)
            return;
        _warned = true;
        Engine?.Log.Info("hyprland", $"GlobalShortcut {_appid}:{_name} will not fire: Winland has no global shortcuts. Bind a key in Winland to run `winshell ipc call` for an IpcHandler instead.");
    }
}

/// <summary>
/// Quickshell's <c>HyprlandFocusGrab</c>: while active, a click outside every listed window clears the grab. The
/// grab starts once one of the windows is visible, and clears when none is.
/// </summary>
[QmlSignal("cleared")]
public sealed unsafe class HyprlandFocusGrab : QObject, IPropertyObserver
{
    private static readonly HashSet<HyprlandFocusGrab> Grabbing = [];
    private static nint _hook;
    private readonly List<QsWindow> _observed = [];
    private bool _active;
    private bool _wanted;

    public HyprlandFocusGrab()
    {
        Windows = new QmlList<QObject>(this, "windows");
        Windows.Changed += OnWindowsChanged;
    }

    [Qml]
    public bool Active
    {
        get { Track(); return _active; }
        set
        {
            _wanted = value;
            if (value)
                TryStart();
            else
                Stop(emitCleared: false);
        }
    }

    [Qml] public QmlList<QObject> Windows { get; }

    private IEnumerable<QsWindow> Listed => Windows.OfType<QsWindow>();

    private bool AnyVisible => Listed.Any(w => w.Backend is { IsVisible: true });

    public void OnPropertyChanged(QObject sender, string property) => Reevaluate();

    private void OnWindowsChanged()
    {
        foreach (var window in _observed)
            window.RemoveObserver("visible", this);
        _observed.Clear();
        foreach (var window in Listed)
        {
            window.AddObserver("visible", this);
            _observed.Add(window);
        }
        Reevaluate();
    }

    private void Reevaluate()
    {
        if (_active && !AnyVisible)
            Stop(emitCleared: true);
        else if (_wanted && !_active)
            TryStart();
    }

    private void TryStart()
    {
        if (_active || IsDestroyed || !AnyVisible)
            return;
        Grabbing.Add(this);
        if (_hook == 0)
            _hook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, &MouseHook, Win32.GetModuleHandleW(null), 0);
        SetField(ref _active, true, nameof(Active));
    }

    private void Stop(bool emitCleared)
    {
        if (!_active)
            return;
        Grabbing.Remove(this);
        if (Grabbing.Count == 0 && _hook != 0)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
        _wanted = false;
        SetField(ref _active, false, nameof(Active));
        if (emitCleared)
            Emit("cleared");
    }

    private void OnClick(POINT pt)
    {
        if (!_active)
            return;
        foreach (var window in Listed)
        {
            if (window.Backend is Win32Window { IsVisible: true } native && Win32.GetWindowRect(native.Handle, out var r)
                && pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom)
                return;
        }
        Stop(emitCleared: true);
    }

    [UnmanagedCallersOnly]
    private static nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (uint)wParam is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN)
        {
            var pt = ((MSLLHOOKSTRUCT*)lParam)->pt;
            foreach (var grab in Grabbing.ToArray())
            {
                if (grab.Engine?.Loop is { } loop)
                    loop.Post(() => grab.OnClick(pt));
            }
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    protected override void OnDestroy()
    {
        foreach (var window in _observed)
            window.RemoveObserver("visible", this);
        _observed.Clear();
        Stop(emitCleared: false);
    }
}

/// <summary>
/// Quickshell's <c>HyprlandWindow</c> attached object. Winland does not compose Winshell's windows, so the values
/// are kept but change nothing.
/// </summary>
public sealed class HyprlandWindowAttached(QObject owner) : QObject
{
    internal QObject Window => owner;

    private double _opacity = 1;
    private Region? _visibleMask;

    [Qml] public double Opacity { get { Track(); return _opacity; } set => SetField(ref _opacity, Math.Clamp(value, 0, 1)); }

    [Qml] public Region? VisibleMask { get { Track(); return _visibleMask; } set => SetField(ref _visibleMask, value); }
}
