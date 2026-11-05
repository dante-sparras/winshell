using System.Collections.Concurrent;
using Winshell.Native;

namespace Winshell.Tray;

/// <summary>
/// Sees the notification area icons of every app, the way an alternative Windows shell does. Apps send their icons to
/// the first <c>Shell_TrayWnd</c> window they find, so this makes one and keeps it in front of Explorer's, then
/// passes every message on to Explorer so its own tray and the app bars keep working. Apps are asked to add their
/// icons again with the <c>TaskbarCreated</c> broadcast.
/// </summary>
internal sealed class TrayHost : ITrayHost, IDisposable
{
    private const uint WM_COPYDATA = 0x004A;
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
    private static readonly ConcurrentDictionary<nint, TrayHost> Hosts = new();

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly TrayNames _names;
    private readonly Action _broadcast;
    // Apps send their icons to one window, so one host in the session at a time is the notification area. The
    // others wait, and the first to notice takes over when it exits, even if it crashed.
    private readonly Mutex _owner;
    private readonly TrayIcons _icons = new(icon => (IconImage.FromHandle(icon), IconImage.ResourceOf(icon)), OwnerOf);
    private readonly TrayClicker _clicker;
    private IDisposable? _settingsWatch;
    private TrayWindow? _window;
    private nint _answered;
    private bool _waiting;
    private IDisposable? _timer;
    private IDisposable? _answer;
    private readonly List<WinEventHook> _eventHooks = [];

    /// <param name="loop">The loop the host's events are raised on.</param>
    /// <param name="log">Where the host writes its warnings.</param>
    /// <param name="names">The window class, the taskbar class and the owner name; tests pass their own.</param>
    /// <param name="broadcast">Asks every app to add its icons again; <see cref="BroadcastTaskbarCreated"/> in a shell.</param>
    public TrayHost(IEventLoop loop, Log log, TrayNames names, Action broadcast)
    {
        _loop = loop;
        _log = log;
        _names = names;
        _broadcast = broadcast;
        _owner = new Mutex(false, names.Owner);
        _clicker = new TrayClicker(loop, TrayClicker.LetAppTakeForeground);
    }

    /// <summary>The notification area window, or 0 while another host owns the tray.</summary>
    public nint Window => _window?.Handle ?? 0;

    public IReadOnlyCollection<TrayIcon> Icons => _icons.Icons;

    public event Action<TrayIcon>? Added
    {
        add => _icons.Added += value;
        remove => _icons.Added -= value;
    }

    public event Action<TrayIcon>? Changed
    {
        add => _icons.Changed += value;
        remove => _icons.Changed -= value;
    }

    public event Action<TrayIcon>? Removed
    {
        add => _icons.Removed += value;
        remove => _icons.Removed -= value;
    }

    public event Action<TrayIcon, string, string, uint>? Balloon
    {
        add => _icons.Balloon += value;
        remove => _icons.Balloon -= value;
    }

    public void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at) => _clicker.Click(icon, click, at);

    /// <summary>Becomes the notification area, or waits for the host that has it to exit. Call on the loop's thread.</summary>
    public void Start()
    {
        _timer = null;
        if (!TryOwn())
        {
            if (!_waiting)
                _log.Info("tray", "Another winshell instance is the notification area; this one takes over when it exits");
            _waiting = true;
            _timer = _loop.Schedule(TimeSpan.FromMilliseconds(500), Start);
            return;
        }
        _waiting = false;
        _window = TrayWindow.Create(_names.Window, OnCopyData, OnMessage);
        if (_window is null)
        {
            _owner.ReleaseMutex();
            _log.Warn("tray", "Could not create the notification area window; the system tray stays empty");
            return;
        }
        Hosts[_window.Handle] = this;
        // Explorer's taskbar lets these through from any integrity level, so an elevated shell still hears Explorer
        // start, and an app at a lower level can still add its icon.
        _window.AllowFromAnyLevel(TaskbarCreated);
        _window.AllowFromAnyLevel(WM_COPYDATA);
        _answered = ExplorerTray();
        _icons.UseSettings(TrayIconSettings.Read());
        _settingsWatch = TrayIconSettings.Watch(_loop, () => _icons.UseSettings(TrayIconSettings.Read()));
        foreach (var eventType in new uint[] { Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_OBJECT_REORDER })
        {
            if (WinEventHook.Start(eventType, OnOrderChanged) is { } hook)
                _eventHooks.Add(hook);
            else
                _log.Warn("tray", "Could not watch taskbar order; the tray will check every thirty seconds.");
        }
        KeepInFront();
        // Every app adds its icons again, now to this window.
        _broadcast();
    }

    /// <summary>
    /// Gives the notification area up: the window goes, the next host may take over, and apps are asked to add
    /// their icons again to whichever taskbar window is first now. Call on the loop's thread.
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _answer?.Dispose();
        _clicker.Dispose();
        _settingsWatch?.Dispose();
        _timer = _answer = _settingsWatch = null;
        foreach (var hook in _eventHooks)
            hook.Dispose();
        _eventHooks.Clear();
        if (_window is not null)
        {
            Hosts.TryRemove(_window.Handle, out _);
            _window.Dispose();
            _window = null;
            _owner.ReleaseMutex();
            _broadcast();
        }
        _owner.Dispose();
    }

    private bool TryOwn()
    {
        try
        {
            return _owner.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The host that had it ended without letting go; this thread owns it now.
            return true;
        }
    }

    /// <summary>
    /// Puts Explorer's taskbar in front of this process's hosts until disposed, so an app bar request this process
    /// sends reaches Explorer itself. When the first <c>Shell_TrayWnd</c> belongs to the calling process,
    /// <c>SHAppBarMessage</c> passes the rectangle by an address in that process, which Explorer cannot use once a host
    /// passes the message on: <c>ABM_QUERYPOS</c> and <c>ABM_SETPOS</c> then do nothing, and the work area stays as it was.
    /// </summary>
    public static IDisposable ExplorerFirst()
    {
        var moved = new List<TrayHost>();
        foreach (var host in Hosts.Values)
        {
            var explorer = host.ExplorerTray();
            if (explorer != 0 && Win32.SetWindowPos(host.Window, explorer, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE))
                moved.Add(host);
        }
        return new Restore(moved);
    }

    private sealed class Restore(List<TrayHost> hosts) : IDisposable
    {
        public void Dispose()
        {
            foreach (var host in hosts)
            {
                if (host.Window != 0)
                    host.Raise();
            }
        }
    }

    /// <summary>Asks every app to register its icons with whichever taskbar window is first.</summary>
    public static void BroadcastTaskbarCreated()
    {
        Win32.PostMessageW(HWND_BROADCAST, TaskbarCreated, 0, 0);
        // A broadcast reaches only top-level windows, and some apps, such as Hex, keep their icons on a message-only window.
        foreach (var window in MessageOnlyWindows())
            Win32.PostMessageW(window, TaskbarCreated, 0, 0);
    }

    /// <summary>
    /// Every message-only window on this desktop, each once, from a list Windows copies at once. A walk from one window
    /// to the next ends early, with no error, where a window closes, and the newest windows, where it starts, come and
    /// go the most.
    /// </summary>
    internal static HashSet<nint> MessageOnlyWindows()
    {
        // Only a message-only window names their parent, and the newest one may close before it is asked.
        nint parent = 0;
        for (var attempt = 0; parent == 0 && attempt < 3; attempt++)
            parent = Win32.GetAncestor(Win32.FindWindowExW(Win32.HWND_MESSAGE, 0, null, null), GA_PARENT);
        var windows = parent != 0 ? WindowTree.Descendants(parent) : [];
        // The list also has the child windows of message-only windows.
        windows.RemoveWhere(hwnd => Win32.GetAncestor(hwnd, GA_PARENT) != parent);
        return windows;
    }

    /// <summary>
    /// Drops icons of apps that ended without removing them, and checks the taskbar order as a fallback for a
    /// missing WinEvent. Order changes normally re-raise the host at once, without a timer.
    /// </summary>
    private void KeepInFront()
    {
        try
        {
            Raise();
            foreach (var icon in _icons.Icons.ToArray())
            {
                if (!Win32.IsWindow(icon.Window))
                    _icons.Remove(icon.Key);
            }
        }
        finally
        {
            if (_window is not null)
                _timer = _loop.Schedule(TimeSpan.FromSeconds(30), KeepInFront);
        }
    }

    // Out-of-context events run on the thread that installed the hook, which owns this host. A failure is dropped; the
    // thirty-second check remains a fallback.
    private void OnOrderChanged(uint eventType, nint hwnd, int idObject, int idChild)
    {
        if (idChild != Win32.CHILDID_SELF || Window == 0)
            return;
        var explorer = ExplorerTray();
        // Top-level z-order changes name the desktop's client object, not a child window's OBJID_WINDOW.
        var desktopOrder = eventType == Win32.EVENT_OBJECT_REORDER && idObject == Win32.OBJID_CLIENT && hwnd == WindowTree.Desktop;
        if (explorer == 0 || !desktopOrder && (hwnd != explorer || idObject != Win32.OBJID_WINDOW))
            return;
        // A raise causes another reorder event. Only raise when Explorer is actually above this host.
        for (var above = Win32.GetWindow(explorer, Win32.GW_HWNDPREV); above != 0; above = Win32.GetWindow(above, Win32.GW_HWNDPREV))
        {
            if (above == Window)
                return;
        }
        Raise();
    }

    private void Raise()
    {
        var explorer = ExplorerTray();
        if (explorer != 0 && Win32.GetWindowRect(explorer, out var r))
        {
            // Apps that read the taskbar's position from this window get Explorer's.
            Win32.SetWindowPos(Window, Win32.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOACTIVATE);
        }
        else
        {
            Win32.SetWindowPos(Window, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Explorer broadcasts <c>TaskbarCreated</c> when it makes a new taskbar: after a restart, a crash or an update.
    /// Apps then add their icons to whichever taskbar window is first, which can be Explorer's new one. This window
    /// gets back in front and asks once more a moment later, so every app registers through it. A broadcast while
    /// Explorer's taskbar is the one already answered is not answered: this host's own, or another host's.
    /// </summary>
    private void OnTaskbarCreated()
    {
        Raise();
        var explorer = ExplorerTray();
        if (explorer == 0 || explorer == _answered)
            return;
        _answered = explorer;
        _log.Info("tray", "Explorer made a new taskbar; asking apps to add their icons again");
        _answer?.Dispose();
        _answer = _loop.Schedule(TimeSpan.FromSeconds(1), () =>
        {
            _answer = null;
            _broadcast();
        });
    }

    /// <summary>Explorer's own taskbar window, found past this one.</summary>
    private nint ExplorerTray()
    {
        nint hwnd = 0;
        while ((hwnd = Win32.FindWindowExW(0, hwnd, _names.Explorer, null)) != 0)
        {
            if (hwnd != Window)
                return hwnd;
        }
        return 0;
    }

    private nint OnCopyData(CopyData data)
    {
        try
        {
            return Answer(data);
        }
#pragma warning disable CA1031 // A malformed message from another app must not take the shell down.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Warn("tray", e.Message);
            return 0;
        }
    }

    private nint Answer(CopyData data)
    {
        var answer = 1;
        if (data.Kind == TrayProtocol.NotifyIconData && TrayProtocol.Read(data.Payload) is { } call)
            answer = _icons.Apply(call) ? 1 : 0;
        // Explorer keeps its own tray, app bars and icon positions: it gets every message. Its answer is the answer to an
        // app bar message; an icon is answered by this host, which may not have an icon Explorer has.
        var explorer = ExplorerTray();
        if (explorer == 0)
            return answer;
        // An icon is answered here, and apps animate theirs several times a second, so a busy Explorer holds the shell
        // up only briefly for one. An app bar needs Explorer's answer.
        var icon = data.Kind == TrayProtocol.NotifyIconData;
        var result = data.Forward(explorer, icon ? 200u : 2000u);
        return icon ? answer : result;
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == TaskbarCreated)
        {
            NativeCallback.Guard(_log, "tray", OnTaskbarCreated);
            return true;
        }
        // Apps that post a menu command to the taskbar, such as minimizing every window, find this window first.
        // These carry no pointers. Private WM_USER messages have no shared layout and cannot be forwarded safely.
        if (msg == Win32.WM_SYSCOMMAND || msg == WM_COMMAND && lParam == 0)
        {
            var explorer = ExplorerTray();
            if (explorer != 0)
                Win32.PostMessageW(explorer, msg, wParam, lParam);
            return true;
        }
        return false;
    }

    private static (int ProcessId, string ProgramPath) OwnerOf(nint window)
    {
        Win32.GetWindowThreadProcessId(window, out var pid);
        return ((int)pid, ProcessImage.TryRead(pid) ?? "");
    }

    private static readonly nint HWND_BROADCAST = 0xffff;
    private const uint GA_PARENT = 1;
    private const uint WM_COMMAND = 0x0111;
}
