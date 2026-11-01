using System.Runtime.InteropServices;
using Winshell.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>
/// <c>FocusGrab</c>: while active, a click outside every listed window clears the grab. The grab starts once one
/// of the windows is visible, and clears when none is.
/// </summary>
[QmlSignal("cleared")]
public sealed unsafe class FocusGrab : QObject, IPropertyObserver
{
    // The hook thread reads Grabbing; the loop threads change it.
    private static readonly object Gate = new();
    private static readonly HashSet<FocusGrab> Grabbing = [];
    private static HookThread? _hook;
    private readonly List<ShellWindow> _observed = [];
    private IEventLoop? _loop;
    private bool _active;
    private bool _wanted;

    public FocusGrab()
    {
        Windows = new QmlList<QObject>(this, "windows");
        Windows.Changed += OnWindowsChanged;
    }

    internal static Thread? InstalledHookThread => _hook?.Thread;

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

    private IEnumerable<ShellWindow> Listed => Windows.OfType<ShellWindow>();

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
        _loop = Engine?.Loop;
        lock (Gate)
            Grabbing.Add(this);
        _hook ??= HookThread.Start(Win32.WH_MOUSE_LL, &MouseHook, "Winshell focus grab hook");
        SetField(ref _active, true, nameof(Active));
    }

    private void Stop(bool emitCleared)
    {
        if (!_active)
            return;
        bool none;
        lock (Gate)
        {
            Grabbing.Remove(this);
            none = Grabbing.Count == 0;
        }
        if (none && _hook is not null)
        {
            _hook.Stop();
            _hook = null;
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
            NativeCallback.Guard(Log.Default, "focusgrab", () =>
            {
                FocusGrab[] grabs;
                lock (Gate)
                    grabs = [.. Grabbing];
                foreach (var grab in grabs)
                {
                    if (grab._loop is { } loop)
                        loop.Post(() => grab.OnClick(pt));
                }
            });
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
