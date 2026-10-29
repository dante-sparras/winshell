using System.Runtime.InteropServices;
using SkiaSharp;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Tray;

/// <summary>One notification area icon an app added with <c>Shell_NotifyIcon</c>.</summary>
internal sealed class TrayIcon
{
    public required string Key { get; init; }

    public nint Window { get; set; }

    public uint Id { get; set; }

    public Guid Guid { get; set; }

    public uint CallbackMessage { get; set; }

    public uint Version { get; set; }

    public string Tip { get; set; } = "";

    /// <summary>The app hid the icon (<c>NIS_HIDDEN</c>); Explorer does not show it.</summary>
    public bool Hidden { get; set; }

    /// <summary>Windows shows the icon in the hidden icons flyout rather than in the taskbar.</summary>
    public bool Overflow { get; set; } = true;

    /// <summary>The icon as the app drew it, at the size it chose.</summary>
    public SKImage? Image { get; set; }

    /// <summary>The module and resource the icon was loaded from, when Windows knows, so it can be drawn at any size.</summary>
    public IconResource? Resource { get; set; }

    public int ImageGeneration { get; set; }

    public int ProcessId { get; set; }

    public string ProgramPath { get; set; } = "";
}

/// <summary>An icon resource in a program or library: a numeric id, or a name when the id is 0.</summary>
internal sealed record IconResource(string Module, ushort Id, string Name);

/// <summary>
/// The window class the host registers, the taskbar class it passes messages on to, and the name that makes one
/// host in the session the owner. Tests use their own, so they never see or disturb the real notification area.
/// </summary>
internal sealed record TrayNames(string Window, string Explorer, string Owner)
{
    public static readonly TrayNames Session = new("Shell_TrayWnd", "Shell_TrayWnd", @"Local\winshell-notification-area");
}

/// <summary>
/// Sees the notification area icons of every app, the way an alternative Windows shell does. Apps send their icons to
/// the first <c>Shell_TrayWnd</c> window they find, so this makes one and keeps it in front of Explorer's, then
/// passes every message on to Explorer so its own tray and the app bars keep working. Apps are asked to add their
/// icons again with the <c>TaskbarCreated</c> broadcast.
/// </summary>
internal sealed unsafe partial class TrayHost : IDisposable
{
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_USER = 0x0400;
    internal const uint MediumIntegrity = 0x2000;
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
    private static readonly uint ShellIntegrity = IntegrityLevel((uint)Environment.ProcessId);
    private static readonly Dictionary<nint, TrayHost> Hosts = [];
    private static readonly HashSet<string> Registered = [];

    private readonly IEventLoop _loop;
    private readonly Log _log;
    private readonly TrayNames _names;
    private readonly Action _broadcast;
    // Apps send their icons to one window, so one host in the session at a time is the notification area. The
    // others wait, and the first to notice takes over when it exits, even if it crashed.
    private readonly Mutex _owner;
    private readonly Dictionary<string, TrayIcon> _icons = [];
    private readonly Dictionary<(nint Window, uint Id), TrayIcon> _byWindow = [];
    private readonly Dictionary<Guid, TrayIcon> _byGuid = [];
    private IReadOnlyList<TrayIconSetting> _settings = [];
    private IDisposable? _settingsWatch;
    private nint _hwnd;
    private nint _answered;
    private bool _waiting;
    private IDisposable? _timer;
    private IDisposable? _answer;
    private IDisposable? _cursorRestore;

    /// <param name="broadcast">Asks every app to add its icons again; <see cref="BroadcastTaskbarCreated"/> in a shell.</param>
    public TrayHost(IEventLoop loop, Log log, TrayNames names, Action broadcast)
    {
        _loop = loop;
        _log = log;
        _names = names;
        _broadcast = broadcast;
        _owner = new Mutex(false, names.Owner);
    }

    /// <summary>The notification area window, or 0 while another host owns the tray.</summary>
    public nint Window => _hwnd;

    public IReadOnlyCollection<TrayIcon> Icons => _icons.Values;

    public event Action<TrayIcon>? Added;

    public event Action<TrayIcon>? Changed;

    public event Action<TrayIcon>? Removed;

    /// <summary>The app asked for a balloon notification: its title, its text, and its <c>NIIF_</c> flags.</summary>
    public event Action<TrayIcon, string, string, uint>? Balloon;

    public TrayIcon? Find(string key) => _icons.GetValueOrDefault(key);

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
        if (Registered.Add(_names.Window))
        {
            fixed (char* name = _names.Window)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = Win32.GetModuleHandleW(null),
                    lpszClassName = name,
                };
                Win32.RegisterClassExW(wc);
            }
        }
        // Layered and transparent so the window can report Explorer's taskbar rectangle without taking its clicks.
        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT,
            _names.Window, "", Win32.WS_POPUP, 0, 0, 0, 0, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (_hwnd == 0)
        {
            _owner.ReleaseMutex();
            _log.Warn("tray", "Could not create the notification area window; the system tray stays empty");
            return;
        }
        Hosts[_hwnd] = this;
        SetLayeredWindowAttributes(_hwnd, 0, 0, LwaAlpha);
        // Explorer's taskbar lets these through from any integrity level, so an elevated shell still hears Explorer
        // start, and an app at a lower level can still add its icon.
        ChangeWindowMessageFilterEx(_hwnd, TaskbarCreated, MSGFLT_ALLOW, 0);
        ChangeWindowMessageFilterEx(_hwnd, WM_COPYDATA, MSGFLT_ALLOW, 0);
        _answered = ExplorerTray();
        UseSettings(TrayIconSettings.Read());
        _settingsWatch = TrayIconSettings.Watch(_loop, () => UseSettings(TrayIconSettings.Read()));
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
        _cursorRestore?.Dispose();
        _settingsWatch?.Dispose();
        _timer = _answer = _cursorRestore = _settingsWatch = null;
        if (_hwnd != 0)
        {
            Hosts.Remove(_hwnd);
            Win32.DestroyWindow(_hwnd);
            _hwnd = 0;
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
            if (explorer != 0 && Win32.SetWindowPos(host._hwnd, explorer, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE))
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
                if (host._hwnd != 0)
                    host.Raise();
            }
        }
    }

    /// <summary>Asks every app to register its icons with whichever taskbar window is first.</summary>
    public static void BroadcastTaskbarCreated() => Win32.PostMessageW(HWND_BROADCAST, TaskbarCreated, 0, 0);

    /// <summary>
    /// Keeps this window ahead of Explorer's taskbar, which puts itself in front when used, so apps keep finding it,
    /// and drops the icons of apps that ended without removing them.
    /// </summary>
    private void KeepInFront()
    {
        try
        {
            Raise();
            foreach (var icon in _icons.Values.ToArray())
            {
                if (!IsWindow(icon.Window))
                    Remove(icon.Key);
            }
        }
        finally
        {
            if (_hwnd != 0)
                _timer = _loop.Schedule(TimeSpan.FromMilliseconds(500), KeepInFront);
        }
    }

    private void Raise()
    {
        var explorer = ExplorerTray();
        if (explorer != 0 && GetWindowRect(explorer, out var r))
        {
            // Apps that read the taskbar's position from this window get Explorer's.
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOACTIVATE);
        }
        else
        {
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
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
        while ((hwnd = FindWindowExW(0, hwnd, _names.Explorer, null)) != 0)
        {
            if (hwnd != _hwnd)
                return hwnd;
        }
        return 0;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!Hosts.TryGetValue(hwnd, out var host))
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        if (msg == WM_COPYDATA)
        {
            try
            {
                return host.OnCopyData(wParam, (COPYDATASTRUCT*)lParam);
            }
#pragma warning disable CA1031 // A malformed message from another app must not take the shell down.
            catch (Exception e)
#pragma warning restore CA1031
            {
                host._log.Warn("tray", e.Message);
                return 0;
            }
        }
        if (msg == TaskbarCreated)
        {
            NativeCallback.Guard(host._log, "tray", host.OnTaskbarCreated);
            return 0;
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private nint OnCopyData(nint sender, COPYDATASTRUCT* data)
    {
        if (data->dwData == TrayProtocol.NotifyIconData && TrayProtocol.Read(new ReadOnlySpan<byte>(data->lpData, (int)data->cbData)) is { } call)
            OnNotifyIcon(call);
        // Explorer keeps its own tray, app bars and icon positions: it gets every message, and its answer is the answer.
        var explorer = ExplorerTray();
        if (explorer == 0)
            return 1;
        SendMessageTimeoutW(explorer, WM_COPYDATA, sender, (nint)data, SMTO_ABORTIFHUNG, 2000, out var result);
        return data->dwData == TrayProtocol.NotifyIconData ? 1 : result;
    }

    internal void OnNotifyIcon(NotifyIconCall call)
    {
        var hasGuid = call.Guid != Guid.Empty;
        var window = call.Window;
        // NIM_SETVERSION often leaves the GUID flag off, so an icon added by GUID is also found by its window.
        var icon = Lookup(window, call.Id, call.Guid);
        var key = icon?.Key ?? (hasGuid ? call.Guid.ToString("N") : $"{(uint)window:x}-{call.Id}");
        switch (call.Message)
        {
            case TrayProtocol.Add:
            case TrayProtocol.Modify:
            {
                if (icon is null && call.Message == TrayProtocol.Modify)
                    return;
                icon ??= new TrayIcon { Key = key };
                Unindex(icon);
                if (window != 0)
                {
                    icon.Window = window;
                    icon.Id = call.Id;
                }
                if (hasGuid)
                    icon.Guid = call.Guid;
                // A system message such as WM_CLOSE is no app's callback, and is never sent.
                if ((call.Flags & TrayProtocol.NIF_MESSAGE) != 0)
                    icon.CallbackMessage = call.CallbackMessage >= WM_USER ? call.CallbackMessage : 0;
                if ((call.Flags & TrayProtocol.NIF_TIP) != 0)
                    icon.Tip = call.Tip;
                if ((call.Flags & TrayProtocol.NIF_STATE) != 0 && (call.StateMask & TrayProtocol.NIS_HIDDEN) != 0)
                    icon.Hidden = (call.State & TrayProtocol.NIS_HIDDEN) != 0;
                if ((call.Flags & TrayProtocol.NIF_ICON) != 0)
                {
                    icon.Image?.Dispose();
                    // The app may destroy the icon once this returns, so it is read now.
                    icon.Image = IconImage(call.Icon);
                    icon.Resource = ResourceOf(call.Icon);
                    icon.ImageGeneration++;
                }
                Index(icon);
                var added = !_icons.ContainsKey(icon.Key);
                if (added)
                {
                    GetWindowThreadProcessId(icon.Window, out var pid);
                    icon.ProcessId = (int)pid;
                    icon.ProgramPath = ProgramPath(pid);
                    _icons[icon.Key] = icon;
                }
                icon.Overflow = TrayIconSettings.InOverflow(_settings, icon.Guid, icon.ProgramPath, icon.Id);
                if (added)
                    Added?.Invoke(icon);
                else
                    Changed?.Invoke(icon);
                // An empty text takes a balloon down; Explorer does that itself.
                if ((call.Flags & TrayProtocol.NIF_INFO) != 0 && call.Info.Length > 0)
                    Balloon?.Invoke(icon, call.InfoTitle, call.Info, call.InfoFlags);
                break;
            }
            case TrayProtocol.Delete:
                if (icon is not null)
                    Remove(icon.Key);
                break;
            case TrayProtocol.SetVersion:
                // The only message where this field is a version; elsewhere it is a balloon timeout.
                if (icon is not null)
                    icon.Version = call.TimeoutOrVersion;
                break;
        }
    }

    /// <summary>Takes Windows' choices for which icons the taskbar shows, and updates the icons they changed.</summary>
    internal void UseSettings(IReadOnlyList<TrayIconSetting> settings)
    {
        _settings = settings;
        foreach (var icon in _icons.Values.ToArray())
        {
            var overflow = TrayIconSettings.InOverflow(settings, icon.Guid, icon.ProgramPath, icon.Id);
            if (overflow == icon.Overflow)
                continue;
            icon.Overflow = overflow;
            Changed?.Invoke(icon);
        }
    }

    /// <summary>An icon added by GUID and later addressed by its window, or the other way around, is the same icon.</summary>
    private TrayIcon? Lookup(nint window, uint id, Guid guid)
    {
        if (guid != Guid.Empty && _byGuid.TryGetValue(guid, out var byGuid))
            return byGuid;
        if (window != 0 && _byWindow.TryGetValue((window, id), out var byWindow))
            return byWindow;
        return null;
    }

    private void Index(TrayIcon icon)
    {
        if (icon.Window != 0)
            _byWindow[(icon.Window, icon.Id)] = icon;
        if (icon.Guid != Guid.Empty)
            _byGuid[icon.Guid] = icon;
    }

    private void Unindex(TrayIcon icon)
    {
        if (icon.Window != 0 && _byWindow.TryGetValue((icon.Window, icon.Id), out var byWindow) && ReferenceEquals(byWindow, icon))
            _byWindow.Remove((icon.Window, icon.Id));
        if (icon.Guid != Guid.Empty && _byGuid.TryGetValue(icon.Guid, out var byGuid) && ReferenceEquals(byGuid, icon))
            _byGuid.Remove(icon.Guid);
    }

    private void Remove(string key)
    {
        if (!_icons.Remove(key, out var icon))
            return;
        Unindex(icon);
        Removed?.Invoke(icon);
        icon.Image?.Dispose();
    }

    // Clicks, sent the way Explorer sends them

    public void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at)
    {
        if (!IsWindow(icon.Window) || icon.CallbackMessage == 0)
            return;
        GetWindowThreadProcessId(icon.Window, out var pid);
        if (!MayClick(ShellIntegrity, IntegrityLevel(pid)))
            return;
        var point = at ?? CursorPosition();
        // Apps read the cursor when they open a menu, and many ignore the point in the message.
        if (at is { } target)
            PlaceCursor(target);
        LetAppTakeForeground(icon, pid);
        foreach (var message in TrayProtocol.ClickMessages(click, icon.Version))
            Send(icon, message, point);
    }

    /// <summary>
    /// Whether a click may reach an app at this integrity level. Any app may add an icon here, naming any window and
    /// callback, and nothing says which app sent it. Explorer clicks icons with an unelevated user's rights, so
    /// Windows keeps its clicks from an elevated app unless that app lets them in. An elevated shell would not be
    /// kept out, so it clicks no app above medium integrity, nor one whose level it cannot read.
    /// </summary>
    internal static bool MayClick(uint shell, uint app) => shell <= MediumIntegrity || app <= MediumIntegrity;

    /// <summary>
    /// Lets the app take the foreground for its menu or window. Nothing is activated for it, so the window the user
    /// works in keeps the keyboard when the app shows nothing.
    /// </summary>
    private static void LetAppTakeForeground(TrayIcon icon, uint pid)
    {
        // A click on a shell window is input this process just received, which lets it pass the right on.
        if (AllowSetForegroundWindow(pid))
            return;
        // A click from IPC or a timer is not. Join the foreground thread long enough to hand the foreground over.
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var thread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thread && AttachThreadInput(foregroundThread, thread, true);
        try
        {
            SetForegroundWindow(icon.Window);
            AllowSetForegroundWindow(pid);
        }
        finally
        {
            if (attached)
                AttachThreadInput(foregroundThread, thread, false);
        }
    }

    /// <summary>Moves the cursor to where the menu should open, then puts it back once the app has read it.</summary>
    private void PlaceCursor((int X, int Y) target)
    {
        GetCursorPos(out var previous);
        SetCursorPos(target.X, target.Y);
        _cursorRestore?.Dispose();
        _cursorRestore = _loop.Schedule(TimeSpan.FromMilliseconds(400), () => SetCursorPos(previous.X, previous.Y));
    }

    private static void Send(TrayIcon icon, uint message, (int X, int Y) point)
    {
        var (wParam, lParam) = TrayProtocol.Callback(icon.Version, icon.Id, message, point.X, point.Y);
        SendNotifyMessageW(icon.Window, icon.CallbackMessage, wParam, lParam);
    }

    private static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    private static string ProgramPath(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return "";
        try
        {
            var buffer = new char[1024];
            var length = buffer.Length;
            return QueryFullProcessImageNameW(process, 0, buffer, ref length) ? new string(buffer, 0, length) : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>A process's mandatory integrity level, or <see cref="uint.MaxValue"/> when it cannot be read.</summary>
    internal static uint IntegrityLevel(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
            return uint.MaxValue;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token))
                return uint.MaxValue;
            try
            {
                // A TOKEN_MANDATORY_LABEL, followed by the label's SID, S-1-16-level.
                var label = stackalloc byte[64];
                if (!GetTokenInformation(token, TokenIntegrityLevel, label, 64, out _))
                    return uint.MaxValue;
                var sid = *(nint*)label;
                return *GetSidSubAuthority(sid, (uint)(*GetSidSubAuthorityCount(sid) - 1));
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Draws an icon into a straight-alpha image. Icons without alpha take it from their mask.</summary>
    /// <summary>Where the icon was loaded from, or null for an icon the app drew or copied.</summary>
    private static IconResource? ResourceOf(nint icon)
    {
        var info = new ICONINFOEXW { cbSize = (uint)sizeof(ICONINFOEXW) };
        if (icon == 0 || !GetIconInfoExW(icon, ref info))
            return null;
        if (info.hbmColor != 0)
            DeleteObject(info.hbmColor);
        if (info.hbmMask != 0)
            DeleteObject(info.hbmMask);
        var module = new string(info.szModName);
        var name = new string(info.szResName);
        return module.Length > 0 && (info.wResID != 0 || name.Length > 0) ? new IconResource(module, info.wResID, name) : null;
    }

    /// <summary>
    /// Draws an icon from its resource at a size in pixels, so it is sharp at any scale. Null when the module or the
    /// resource is gone. The library is only mapped as data.
    /// </summary>
    public static SKImage? ResourceImage(IconResource resource, int size)
    {
        var module = LoadLibraryExW(resource.Module, 0, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module == 0)
            return null;
        try
        {
            // Windows picks the image in the icon group closest to the size, and scales it only if none matches.
            var icon = resource.Id != 0
                ? LoadImageW(module, resource.Id, IMAGE_ICON, size, size, 0)
                : LoadImageW(module, resource.Name, IMAGE_ICON, size, size, 0);
            if (icon == 0)
                return null;
            try
            {
                return IconImage(icon, size);
            }
            finally
            {
                DestroyIcon(icon);
            }
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>Draws an icon into a straight-alpha image, at its own size unless one is given.</summary>
    private static SKImage? IconImage(nint icon, int size = 0)
    {
        if (icon == 0 || !GetIconInfo(icon, out var info))
            return null;
        try
        {
            if (size <= 0)
            {
                size = 32;
                if (GetObjectW(info.hbmColor != 0 ? info.hbmColor : info.hbmMask, sizeof(BITMAP), out var bmp) != 0)
                    size = Math.Clamp(bmp.bmWidth, 16, 256);
            }
            var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
            var dc = CreateCompatibleDC(0);
            var dib = CreateDIBSection(dc, &header, 0, out var bits, 0, 0);
            if (dib == 0)
            {
                DeleteDC(dc);
                return null;
            }
            var old = SelectObject(dc, dib);
            DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_NORMAL);
            var pixels = new Span<uint>((void*)bits, size * size);
            var hasAlpha = false;
            foreach (var p in pixels)
            {
                if ((p & 0xff000000) != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }
            if (!hasAlpha)
            {
                // An old icon: draw its mask, which is black where the icon is opaque.
                var mask = new uint[size * size];
                DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_MASK);
                pixels.CopyTo(mask);
                DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, DI_NORMAL);
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = (mask[i] & 0xffffff) == 0 ? pixels[i] | 0xff000000 : 0;
            }
            var image = SKImage.FromPixelCopy(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul), bits, size * 4);
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
            return image;
        }
        finally
        {
            if (info.hbmColor != 0)
                DeleteObject(info.hbmColor);
            if (info.hbmMask != 0)
                DeleteObject(info.hbmMask);
        }
    }

    // Native

    private static readonly nint HWND_BROADCAST = 0xffff;
    private const uint SMTO_ABORTIFHUNG = 0x2;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const uint DI_MASK = 0x1, DI_NORMAL = 0x3;
    private const uint LwaAlpha = 0x2;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2, LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    private const uint IMAGE_ICON = 1;
    private const uint MSGFLT_ALLOW = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public nuint dwData;
        public uint cbData;
        public void* lpData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFOEXW
    {
        public uint cbSize;
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
        public ushort wResID;
        public fixed char szModName[260];
        public fixed char szResName[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_POINT
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out NATIVE_RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint changeFilter);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SendNotifyMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attachTo, uint attachFrom, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NATIVE_POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, void* info, int length, out int returned);

    [LibraryImport("advapi32.dll")]
    private static partial byte* GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial uint* GetSidSubAuthority(nint sid, uint index);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint process, uint flags, [Out] char[] name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfoExW(nint icon, ref ICONINFOEXW info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial nint LoadImageW(nint module, nint name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImageW(nint module, string name, uint type, int width, int height, uint flags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, out BITMAP bitmap);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
