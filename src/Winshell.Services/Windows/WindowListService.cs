using System.Collections.ObjectModel;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>What Winshell knows about one window on the taskbar.</summary>
internal sealed record WindowInfo(
    nint Hwnd,
    string Title,
    string AppId,
    string Executable,
    string Icon,
    nint Owner,
    bool Maximized,
    bool Minimized,
    bool Fullscreen,
    string Monitor);

/// <summary>
/// The app id kept for a window between reads. <see cref="PackageKnown"/> is true once the process package has been
/// asked, including when it has none.
/// </summary>
internal readonly record struct WindowIdentity(
    string AppId,
    string Executable,
    string Icon,
    string PackageAppId,
    bool PackageKnown);

/// <summary>The other programs' windows, as the taskbar and Alt+Tab list them.</summary>
public sealed class WindowListService : ObservableObject, IDisposable
{
    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly IWindowBackend _backend;
    private readonly IDisposable _winEvents;
    private readonly IDisposable _flashes;
    private readonly VirtualDesktopsService? _desktops;
    private readonly Dictionary<nint, AppWindow> _byHwnd = [];
    private readonly Dictionary<nint, WindowIdentity> _identities = [];
    private readonly HashSet<nint> _dirty = [];
    private readonly HashSet<nint> _flashed = [];
    private readonly HashSet<nint> _wanting = [];
    private AppWindow? _activeWindow;
    private bool _flushPosted;
    private bool _foregroundDirty;
    private bool _disposed;

    /// <param name="loop">The loop of the thread the windows are read on. That thread must pump messages.</param>
    /// <param name="log">Where a hook that cannot be set, a failed event and a failed move are reported.</param>
    /// <param name="desktops">Gives <see cref="AppWindow.Desktop"/> its value. Without it, that stays null.</param>
    /// <param name="backend">The windows, for a test.</param>
    internal WindowListService(IEventLoop loop, Log log, VirtualDesktopsService? desktops = null,
        IWindowBackend? backend = null)
    {
        _loop = loop;
        _log = log;
        _desktops = desktops;
        _backend = backend ?? WindowBackend.Shared;
        _winEvents = _backend.ListenWinEvents(OnWinEvent, Failed);
        _flashes = _backend.ListenFlashes(OnFlashed, Failed);
        foreach (var refused in _backend.Start())
            log.Warn("windows", refused);
        if (_desktops is not null)
            _desktops.Refreshed += OnDesktopsRefreshed;
        foreach (var hwnd in _backend.TopLevel())
            _dirty.Add(hwnd);
        _foregroundDirty = true;
        Flush();
    }

    /// <summary>The windows, in the order they opened.</summary>
    public ObservableCollection<AppWindow> Windows { get; } = [];

    /// <summary>The listed window that has the focus, or owns the window that has it. Null when none does.</summary>
    public AppWindow? ActiveWindow => _activeWindow;

    /// <summary>The window with this handle, or null when it is not listed.</summary>
    internal AppWindow? Find(nint hwnd)
    {
        return _byHwnd.GetValueOrDefault(hwnd);
    }

    private void OnWinEvent(uint eventType, nint hwnd, int idObject, int idChild)
    {
        // Windows reports no move of a window on its own, so maximizing, restoring and moving to another monitor
        // arrive as location changes. The caret and cursor send them too, on every move, so drop those first.
        if (eventType == Win32.EVENT_OBJECT_LOCATIONCHANGE && !WindowRules.IsWindowObject(idObject, idChild))
            return;
        var isRoot = hwnd != 0 && eventType != Win32.EVENT_OBJECT_DESTROY && _backend.RootOf(hwnd) == hwnd;
        var marksWindow =
            WindowRules.MarksWindow(eventType, hwnd, idObject, idChild, _byHwnd.ContainsKey(hwnd), isRoot);
        var marksForeground = WindowRules.MarksForeground(eventType);
        if (!marksWindow && !marksForeground)
            return;
        if (marksWindow)
            _dirty.Add(hwnd);
        _foregroundDirty |= marksForeground;
        PostFlush();
    }

    private void Failed(Exception e)
    {
        _log.Warn("windows", e.Message);
    }

    private void OnFlashed(nint hwnd)
    {
        _flashed.Add(hwnd);
        PostFlush();
    }

    private void PostFlush()
    {
        if (_flushPosted)
            return;
        _flushPosted = true;
        _loop.Post(Flush);
    }

    /// <summary>Reads each window marked since the last turn of the loop once, and the foreground when it may have moved.</summary>
    private void Flush()
    {
        _flushPosted = false;
        if (_disposed)
            return;
        // A read can pump once the UI thread is STA. Take only the windows marked so far, each just before it is read,
        // so a window marked during a read waits for the flush its event posts.
        var added = new List<AppWindow>();
        foreach (var hwnd in _dirty.ToArray())
        {
            _dirty.Remove(hwnd);
            var info = Read(hwnd);
            _byHwnd.TryGetValue(hwnd, out var window);
            if (info is null)
            {
                _identities.Remove(hwnd);
                if (window is not null)
                    Remove(window);
            }
            else if (window is null)
            {
                window = new AppWindow(info, _backend, _desktops, _log);
                _byHwnd[hwnd] = window;
                added.Add(window);
            }
            else
            {
                window.Update(info);
            }
        }

        foreach (var window in _byHwnd.Values)
            window.SetParent(window.Owner == 0 ? null : _byHwnd.GetValueOrDefault(window.Owner));
        foreach (var window in added)
            Windows.Add(window);
        if (_foregroundDirty)
        {
            _foregroundDirty = false;
            Set(ref _activeWindow, ActiveOf(_backend.Foreground), nameof(ActiveWindow));
        }

        // A dialog that flashes asks for the window that owns it. The window in front already has the user.
        foreach (var hwnd in _flashed)
            if (ActiveOf(hwnd) is { } wanting && wanting != _activeWindow)
                _wanting.Add(wanting.Hwnd);
        _flashed.Clear();
        WindowRules.SettleAttention(_wanting, _activeWindow?.Hwnd ?? 0, _byHwnd.ContainsKey);
        foreach (var window in _byHwnd.Values)
        {
            window.SetActivated(window == _activeWindow);
            window.SetAttention(_wanting.Contains(window.Hwnd));
        }
    }

    private void Remove(AppWindow window)
    {
        _byHwnd.Remove(window.Hwnd);
        Windows.Remove(window);
        if (_activeWindow == window)
            Set(ref _activeWindow, null, nameof(ActiveWindow));
        window.NotifyClosed();
    }

    /// <summary>The listed window that is this one or owns it, as a main window owns its dialog. Null when none is.</summary>
    private AppWindow? ActiveOf(nint hwnd)
    {
        hwnd = hwnd == 0 ? 0 : _backend.RootOf(hwnd);
        for (var depth = 0; hwnd != 0 && depth < 16; depth++)
        {
            if (_byHwnd.TryGetValue(hwnd, out var window))
                return window;
            hwnd = _backend.OwnerOf(hwnd);
        }

        return null;
    }

    /// <summary>
    /// Reads a window, or null when the taskbar does not show it. The package id and the program path do not change,
    /// so each is read once.
    /// </summary>
    private WindowInfo? Read(nint hwnd)
    {
        if (!ShowsOnTaskbar(_backend.TaskbarFacts(hwnd)))
            return null;
        _identities.TryGetValue(hwnd, out var previous);
        var read = _backend.Read(hwnd, !previous.PackageKnown, previous.Executable is not { Length: > 0 });
        var identity = Resolve(previous, read.AppUserModelId, read.PackageAppId, read.Executable);
        _identities[hwnd] = identity;
        return new WindowInfo(hwnd, read.Title, identity.AppId, identity.Executable, identity.Icon,
            _backend.OwnerOf(hwnd),
            read.Maximized, read.Minimized, read.Fullscreen, read.Monitor);
    }

    /// <summary>
    /// The taskbar's rule: a visible, uncloaked top-level window with a title that either asks for a taskbar button
    /// or has no owner and is not a tool window or a window that never activates.
    /// </summary>
    private static bool ShowsOnTaskbar(TaskbarWindowFacts window)
    {
        return window.Exists && window.Visible && !window.Shell && !window.Child
               && (window.AppWindow || (!window.Owned && !window.ToolWindow && !window.NoActivate))
               && window.HasTitle && !window.Cloaked;
    }

    /// <summary>
    /// The app id to keep after one read of the window. <paramref name="windowProperty"/> is the property store now,
    /// and it replaces a package id or a program name. An empty property uses <paramref name="packageAppId"/> when
    /// that was read, or the package id already known. A null executable keeps the path already read.
    /// </summary>
    private static WindowIdentity Resolve(WindowIdentity previous, string windowProperty, string? packageAppId,
        string? executable)
    {
        var package = packageAppId ?? previous.PackageAppId ?? "";
        var path = executable ?? previous.Executable ?? "";
        var source = windowProperty.Length > 0 ? windowProperty : package;
        return new WindowIdentity(
            WindowRules.AppId(windowProperty, package, path),
            path,
            WindowRules.IconPath(source, path),
            package,
            previous.PackageKnown || packageAppId is not null);
    }

    private void OnDesktopsRefreshed()
    {
        foreach (var window in _byHwnd.Values)
            window.NotifyDesktop();
    }

    /// <summary>Stops following the windows. The list stays as it was.</summary>
    public void Dispose()
    {
        _disposed = true;
        _winEvents.Dispose();
        _flashes.Dispose();
        if (_desktops is not null)
            _desktops.Refreshed -= OnDesktopsRefreshed;
    }
}

/// <summary>A top-level window of another program.</summary>
public sealed class AppWindow : ObservableObject
{
    private readonly IWindowBackend _backend;
    private readonly VirtualDesktopsService? _desktops;
    private readonly Log _log;
    private WindowInfo _info;
    private AppWindow? _parent;
    private bool _activated;
    private bool _attention;

    internal AppWindow(WindowInfo info, IWindowBackend backend, VirtualDesktopsService? desktops, Log log)
    {
        _info = info;
        _backend = backend;
        _desktops = desktops;
        _log = log;
    }

    internal nint Hwnd => _info.Hwnd;

    internal nint Owner => _info.Owner;

    /// <summary>The window's title, as its title bar shows it. May be empty.</summary>
    public string Title => _info.Title;

    /// <summary>The AppUserModelID the window carries, or its process package's, or its program's name in lower case.</summary>
    public string AppId => _info.AppId;

    /// <summary>The full path of the program that owns the window, or empty when it cannot be read.</summary>
    public string Executable => _info.Executable;

    /// <summary>A path an icon is read from: a packaged app's Start icon, or the program's own icon.</summary>
    public string Icon => _info.Icon;

    /// <summary>The virtual desktop this window is on, or empty when Windows does not report one. This uses the supported desktop API.</summary>
    public string DesktopId => _backend.DesktopId(Hwnd);

    /// <summary>The desktop in <see cref="VirtualDesktopsService.Desktops"/> whose id is <see cref="DesktopId"/>, when that list is loaded.</summary>
    public VirtualDesktop? Desktop => _desktops?.Find(DesktopId);

    /// <summary>Whether this is <see cref="WindowListService.ActiveWindow"/>.</summary>
    public bool Activated => _activated;

    /// <summary>
    /// Whether the window flashed to ask for attention, as a chat or a finished download does, and has not been
    /// activated since. The taskbar shows this as a highlighted button.
    /// </summary>
    public bool Attention => _attention;

    /// <summary>The listed window that owns this one, as a main window owns its dialog, or null.</summary>
    public AppWindow? Parent => _parent;

    /// <summary>The screen the window is on: the one Windows says it is nearest.</summary>
    public IReadOnlyList<ScreenInfo> Screens => _backend.Screen(_info.Monitor) is { } screen ? [screen] : [];

    /// <summary>Whether the window is maximized. Setting it maximizes the window, or restores it.</summary>
    public bool Maximized
    {
        get => _info.Maximized;
        set => _backend.SetMaximized(Hwnd, value);
    }

    /// <summary>Whether the window is minimized. Setting it minimizes the window, or restores it.</summary>
    public bool Minimized
    {
        get => _info.Minimized;
        set => _backend.SetMinimized(Hwnd, value);
    }

    /// <summary>Whether the window covers its screen without a frame. Windows has no request that puts another program's window into fullscreen.</summary>
    public bool Fullscreen => _info.Fullscreen;

    /// <summary>The window closed. This comes after it has left <see cref="WindowListService.Windows"/>.</summary>
    public event EventHandler? Closed;

    /// <summary>Brings the window to the front and focuses it, restoring it first when it is minimized.</summary>
    public void Activate()
    {
        _backend.Activate(Hwnd);
    }

    /// <summary>Asks the window to close, as its close button does. The program can still keep it open.</summary>
    public void Close()
    {
        _backend.Close(Hwnd);
    }

    /// <summary>Moves the window onto a monitor. Its logical size stays, and a maximized window is maximized again there.</summary>
    public void MoveTo(ScreenInfo? screen)
    {
        _backend.MoveToMonitor(Hwnd, screen?.Id ?? "");
    }

    /// <summary>Moves the window onto a virtual desktop. This uses the supported desktop API.</summary>
    public void MoveToDesktop(VirtualDesktop? desktop)
    {
        if (desktop is null || !Guid.TryParse(desktop.Id, out var id) || id == Guid.Empty)
            return;
        if (_backend.MoveToDesktop(Hwnd, id, out var problem))
            NotifyDesktop();
        else
            _log.Warn("desktops", problem);
    }

    internal void NotifyDesktop()
    {
        Changed(nameof(DesktopId));
        Changed(nameof(Desktop));
    }

    internal void NotifyClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    internal void Update(WindowInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.Title != info.Title, nameof(Title));
        Changed(old.AppId != info.AppId, nameof(AppId));
        Changed(old.Executable != info.Executable, nameof(Executable));
        Changed(old.Icon != info.Icon, nameof(Icon));
        Changed(old.Maximized != info.Maximized, nameof(Maximized));
        Changed(old.Minimized != info.Minimized, nameof(Minimized));
        Changed(old.Fullscreen != info.Fullscreen, nameof(Fullscreen));
        Changed(old.Monitor != info.Monitor, nameof(Screens));
    }

    internal void SetParent(AppWindow? parent)
    {
        Set(ref _parent, parent, nameof(Parent));
    }

    internal void SetActivated(bool activated)
    {
        Set(ref _activated, activated, nameof(Activated));
    }

    internal void SetAttention(bool attention)
    {
        Set(ref _attention, attention, nameof(Attention));
    }

    /// <summary>The window's app id and title, as <c>AppWindow(&lt;app id&gt;: &lt;title&gt;)</c>, for logs.</summary>
    public override string ToString()
    {
        return $"AppWindow({_info.AppId}: {_info.Title})";
    }
}
