using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using Winshell.Native;

namespace Winshell.Tray;

/// <summary>
/// The notification area the way an alternative Windows shell has it. Apps send their icons to the first
/// <c>Shell_TrayWnd</c> window they find, so this makes one, and passes every message on to Explorer so its own tray
/// and the app bars keep working.
/// </summary>
/// <param name="loop">The loop the settings are watched on.</param>
/// <param name="log">Where the backend writes its warnings.</param>
/// <param name="names">The window class, the taskbar class and the owner name; tests pass their own.</param>
internal sealed class TrayBackend(IEventLoop loop, Log log, TrayNames names) : ITrayBackend
{
    // Apps send their icons to one window, so one instance in the session at a time is the notification area. The
    // others wait, and the first to notice takes over when it exits, even if it crashed.
    private readonly Mutex _owner = new(false, names.Owner);
    private readonly List<WinEventHook> _eventHooks = [];
    private bool _owned;
    private TrayWindow? _window;
    private Func<NotifyIconCall, bool>? _apply;
    private IDisposable? _settingsWatch;

    public event Action? TaskbarCreated;

    public event Action? ExplorerInFront;

    public event Action? SettingsChanged;

    /// <summary>The notification area window, or 0 while it is closed.</summary>
    public nint Window => _window?.Handle ?? 0;

    /// <summary>Explorer's own taskbar window, found past this one.</summary>
    public nint Explorer
    {
        get
        {
            nint hwnd = 0;
            while ((hwnd = Win32.FindWindowExW(0, hwnd, names.Explorer, null)) != 0)
                if (hwnd != Window)
                    return hwnd;
            return 0;
        }
    }

    public (int X, int Y) Cursor
    {
        get
        {
            Win32.GetCursorPos(out var p);
            return (p.X, p.Y);
        }
    }

    public bool TryOwn()
    {
        try
        {
            _owned = _owned || _owner.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The instance that had it ended without letting go; this thread owns it now.
            _owned = true;
        }

        return _owned;
    }

    public bool Open(Func<NotifyIconCall, bool> apply)
    {
        _apply = apply;
        _window = TrayWindow.Create(names.Window, OnCopyData, OnMessage, Failed);
        if (_window is null)
            return false;
        _settingsWatch = TrayIconSettings.Watch(loop, () => SettingsChanged?.Invoke());
        foreach (var eventType in new uint[] { Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_OBJECT_REORDER })
            if (WinEventHook.Start(eventType, OnOrderChanged, Failed) is { } hook)
                _eventHooks.Add(hook);
            else
                log.Warn("tray", "Could not watch the taskbar's order");
        return true;
    }

    public void Close()
    {
        _settingsWatch?.Dispose();
        _settingsWatch = null;
        foreach (var hook in _eventHooks)
            hook.Dispose();
        _eventHooks.Clear();
        _window?.Dispose();
        _window = null;
        if (_owned)
            _owner.ReleaseMutex();
        _owned = false;
    }

    public void Dispose()
    {
        Close();
        _owner.Dispose();
    }

    public void Raise()
    {
        var explorer = Explorer;
        if (explorer != 0 && Win32.GetWindowRect(explorer, out var r))
            // Apps that read the taskbar's position from this window get Explorer's.
            Win32.SetWindowPos(Window, Win32.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                (uint)SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        else
            Win32.SetWindowPos(Window, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                (uint)(SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                       SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE));
    }

    public bool Lower()
    {
        var explorer = Explorer;
        return Window != 0 && explorer != 0 && Win32.SetWindowPos(Window, explorer, 0, 0, 0, 0,
            (uint)(SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                   SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE));
    }

    public void Broadcast()
    {
        TrayApp.AskForIcons();
    }

    public IReadOnlyList<TrayIconSetting> ReadSettings()
    {
        return TrayIconSettings.Read();
    }

    public (PixelImage? Image, IconResource? Resource) ReadIcon(nint icon)
    {
        return (IconImage.FromHandle(icon), IconImage.ResourceOf(icon));
    }

    public PixelImage? DrawIcon(IconResource resource, int size)
    {
        return IconImage.FromResource(resource, size);
    }

    public uint ProcessOf(nint window)
    {
        Win32.GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    public string ProgramOf(nint window)
    {
        return ProcessImage.TryRead(ProcessOf(window)) ?? "";
    }

    public uint? IntegrityOf(uint processId)
    {
        return ProcessIntegrity.TryRead(processId);
    }

    public void MoveCursor((int X, int Y) point)
    {
        Native.Cursor.MoveTo(point.X, point.Y);
    }

    public void LetAppTakeForeground(nint window, uint processId)
    {
        // A click on a shell window is input this process just received, which lets it pass the right on.
        if (TrayApp.AllowForeground(processId))
            return;
        // A click from IPC or a timer is not. Take the window, then pass the right on in case the app asks too.
        Focus.Take(window);
        TrayApp.AllowForeground(processId);
    }

    public void Notify(nint window, uint message, nint wParam, nint lParam)
    {
        TrayApp.Notify(window, message, wParam, lParam);
    }

    // Out-of-context events run on the thread that installed the hook, the loop's.
    private void OnOrderChanged(uint eventType, nint hwnd, int idObject, int idChild)
    {
        if (idChild != Win32.CHILDID_SELF || Window == 0)
            return;
        var explorer = Explorer;
        // Top-level z-order changes name the desktop's client object, not a child window's OBJID_WINDOW.
        var desktopOrder = eventType == Win32.EVENT_OBJECT_REORDER && idObject == Win32.OBJID_CLIENT &&
                           hwnd == WindowTree.Desktop;
        if (explorer == 0 || (!desktopOrder && (hwnd != explorer || idObject != Win32.OBJID_WINDOW)))
            return;
        // A raise causes another reorder event. Only signal when Explorer is actually above this window.
        for (var above = Win32.GetWindow(explorer, Win32.GW_HWNDPREV);
             above != 0;
             above = Win32.GetWindow(above, Win32.GW_HWNDPREV))
            if (above == Window)
                return;
        ExplorerInFront?.Invoke();
    }

    private void Failed(Exception e)
    {
        log.Warn("tray", e.Message);
    }

    private nint OnCopyData(CopyData data)
    {
        var answer = 1;
        if (data.Kind == TrayProtocol.NotifyIconData && TrayProtocol.Read(data.Payload) is { } call)
            answer = _apply!(call) ? 1 : 0;
        // Explorer keeps its own tray, app bars and icon positions: it gets every message. Its answer is the answer to an
        // app bar message; an icon is answered here, by a tray which may not have an icon Explorer has.
        var explorer = Explorer;
        if (explorer == 0)
            return answer;
        // Apps animate their icons several times a second, so a busy Explorer holds the shell up only briefly for one.
        // An app bar needs Explorer's answer.
        var icon = data.Kind == TrayProtocol.NotifyIconData;
        var result = data.Forward(explorer, icon ? 200u : 2000u);
        return icon ? answer : result;
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == TaskbarWindows.Created)
        {
            TaskbarCreated?.Invoke();
            return true;
        }

        // Apps that post a menu command to the taskbar, such as minimizing every window, find this window first.
        // These carry no pointers. Private WM_USER messages have no shared layout and cannot be forwarded safely.
        if (msg == Win32.WM_SYSCOMMAND || (msg == PInvoke.WM_COMMAND && lParam == 0))
        {
            var explorer = Explorer;
            if (explorer != 0)
                Win32.PostMessageW(explorer, msg, wParam, lParam);
            return true;
        }

        return false;
    }
}
