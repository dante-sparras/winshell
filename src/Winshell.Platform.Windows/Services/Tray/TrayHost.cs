using System.Runtime.InteropServices;
using SkiaSharp;
using Winshell.Platform.Windows.Native;

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

    public bool Hidden { get; set; }

    public SKImage? Image { get; set; }

    public int ImageGeneration { get; set; }

    public int ProcessId { get; set; }

    public string ProgramPath { get; set; } = "";
}

/// <summary>
/// Sees the notification area icons of every app, the way an alternative Windows shell does. Apps send their icons to
/// the first <c>Shell_TrayWnd</c> window they find, so this makes one and keeps it in front of Explorer's, then
/// passes every message on to Explorer so its own tray and the app bars keep working. Apps are asked to add their
/// icons again with the <c>TaskbarCreated</c> broadcast.
/// </summary>
internal sealed unsafe partial class TrayHost
{
    private const string ClassName = "Shell_TrayWnd";
    private const uint WM_COPYDATA = 0x004A;
    private const int TrayDataSignature = 0x34753423;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_GUID = 0x20;
    private const uint NIS_HIDDEN = 0x1;

    // Apps send their icons to one window, so one instance in the session at a time is the notification area. The
    // others wait, and the first to notice takes over when it exits, even if it crashed.
    private static readonly Mutex Owner = new(false, @"Local\winshell-notification-area");

    private static TrayHost? _instance;
    private readonly WindowsPlatform _platform;
    private readonly Dictionary<string, TrayIcon> _icons = [];
    private readonly Dictionary<(nint Window, uint Id), TrayIcon> _byWindow = [];
    private readonly Dictionary<Guid, TrayIcon> _byGuid = [];
    private nint _hwnd;
    private IDisposable? _timer;
    private IDisposable? _cursorRestore;

    private TrayHost(WindowsPlatform platform) => _platform = platform;

    /// <summary>The host, started the first time a shell uses the system tray.</summary>
    public static TrayHost Get(WindowsPlatform platform)
    {
        if (_instance is null)
        {
            _instance = new TrayHost(platform);
            TrayIconProvider.Host = _instance;
            _instance.Start();
        }
        return _instance;
    }

    public IReadOnlyCollection<TrayIcon> Icons => _icons.Values;

    public event Action<TrayIcon>? Added;

    public event Action<TrayIcon>? Changed;

    public event Action<TrayIcon>? Removed;

    public TrayIcon? Find(string key) => _icons.GetValueOrDefault(key);

    private void Start()
    {
        if (!TryOwn())
        {
            if (_timer is null)
                _platform.Log.Info("tray", "Another winshell instance is the notification area; this one takes over when it exits");
            _timer = _platform.Loop.Schedule(TimeSpan.FromSeconds(2), Start);
            return;
        }
        fixed (char* name = ClassName)
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
        // Layered and transparent so the window can report Explorer's taskbar rectangle without taking its clicks.
        _hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT,
            ClassName, "", Win32.WS_POPUP, 0, 0, 0, 0, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (_hwnd == 0)
        {
            _platform.Log.Warn("tray", "Could not create the notification area window; the system tray stays empty");
            return;
        }
        SetLayeredWindowAttributes(_hwnd, 0, 0, LwaAlpha);
        KeepInFront();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => BroadcastTaskbarCreated();
        // Every app adds its icons again, now to this window.
        BroadcastTaskbarCreated();
    }

    private static bool TryOwn()
    {
        try
        {
            return Owner.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The instance that had it ended without letting go; this thread owns it now.
            return true;
        }
    }

    /// <summary>Asks every app to register its icons with whichever taskbar window is first.</summary>
    private static void BroadcastTaskbarCreated() =>
        Win32.PostMessageW(HWND_BROADCAST, Win32.RegisterWindowMessageW("TaskbarCreated"), 0, 0);

    /// <summary>
    /// Keeps this window ahead of Explorer's taskbar, which puts itself in front when used, so apps keep finding it,
    /// and drops the icons of apps that ended without removing them.
    /// </summary>
    private void KeepInFront()
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
        foreach (var icon in _icons.Values.ToArray())
        {
            if (!IsWindow(icon.Window))
                Remove(icon.Key);
        }
        _timer = _platform.Loop.Schedule(TimeSpan.FromMilliseconds(500), KeepInFront);
    }

    /// <summary>Explorer's own taskbar window, found past this one.</summary>
    private nint ExplorerTray()
    {
        nint hwnd = 0;
        while ((hwnd = FindWindowExW(0, hwnd, ClassName, null)) != 0)
        {
            if (hwnd != _hwnd)
                return hwnd;
        }
        return 0;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_COPYDATA && _instance is { } host && hwnd == host._hwnd)
        {
            try
            {
                return host.OnCopyData(wParam, (COPYDATASTRUCT*)lParam);
            }
#pragma warning disable CA1031 // A malformed message from another app must not take the shell down.
            catch (Exception e)
#pragma warning restore CA1031
            {
                host._platform.Log.Warn("tray", e.Message);
                return 0;
            }
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private nint OnCopyData(nint sender, COPYDATASTRUCT* data)
    {
        if (data->dwData == 1 && data->cbData >= 8 + 24)
        {
            var tray = (SHELLTRAYDATA*)data->lpData;
            if (tray->dwSignature == TrayDataSignature)
            {
                // Older apps send a shorter structure: copy what was sent over a zeroed one.
                var nid = default(NOTIFYICONDATA32);
                var size = (int)Math.Min(data->cbData - 8, (uint)sizeof(NOTIFYICONDATA32));
                Buffer.MemoryCopy(&tray->nid, &nid, sizeof(NOTIFYICONDATA32), size);
                OnNotifyIcon(tray->dwMessage, &nid, size);
            }
        }
        // Explorer keeps its own tray, app bars and icon positions: it gets every message, and its answer is the answer.
        var explorer = ExplorerTray();
        if (explorer == 0)
            return 1;
        SendMessageTimeoutW(explorer, WM_COPYDATA, sender, (nint)data, SMTO_ABORTIFHUNG, 2000, out var result);
        return data->dwData == 1 ? 1 : result;
    }

    private void OnNotifyIcon(uint message, NOTIFYICONDATA32* nid, int size)
    {
        var hasGuid = (nid->uFlags & NIF_GUID) != 0 && size >= (int)Marshal.OffsetOf<NOTIFYICONDATA32>(nameof(NOTIFYICONDATA32.hBalloonIcon));
        var window = (nint)(int)nid->hWnd;
        var guid = hasGuid ? nid->guidItem : Guid.Empty;
        // NIM_SETVERSION often leaves the GUID flag off, so an icon added by GUID is also found by its window.
        var icon = Lookup(window, nid->uID, guid);
        var key = icon?.Key ?? (hasGuid ? guid.ToString("N") : $"{nid->hWnd:x}-{nid->uID}");
        switch (message)
        {
            case NIM_ADD:
            case NIM_MODIFY:
            {
                if (icon is null && message == NIM_MODIFY)
                    return;
                icon ??= new TrayIcon { Key = key };
                Unindex(icon);
                if (window != 0)
                {
                    icon.Window = window;
                    icon.Id = nid->uID;
                }
                if (hasGuid)
                    icon.Guid = guid;
                // The same field is a balloon timeout on other messages. Only 3 and 4 are interface versions.
                if (nid->uTimeoutOrVersion is TrayProtocol.Version or TrayProtocol.Version4)
                    icon.Version = nid->uTimeoutOrVersion;
                if ((nid->uFlags & NIF_MESSAGE) != 0)
                    icon.CallbackMessage = nid->uCallbackMessage;
                if ((nid->uFlags & NIF_TIP) != 0)
                    icon.Tip = new string(nid->szTip).TrimEnd('\0');
                if ((nid->uFlags & NIF_STATE) != 0 && (nid->dwStateMask & NIS_HIDDEN) != 0)
                    icon.Hidden = (nid->dwState & NIS_HIDDEN) != 0;
                if ((nid->uFlags & NIF_ICON) != 0)
                {
                    icon.Image?.Dispose();
                    icon.Image = IconImage((nint)(int)nid->hIcon);
                    icon.ImageGeneration++;
                }
                Index(icon);
                if (!_icons.ContainsKey(icon.Key))
                {
                    GetWindowThreadProcessId(icon.Window, out var pid);
                    icon.ProcessId = (int)pid;
                    icon.ProgramPath = ProgramPath(pid);
                    _icons[icon.Key] = icon;
                    Added?.Invoke(icon);
                }
                else
                {
                    Changed?.Invoke(icon);
                }
                break;
            }
            case NIM_DELETE:
                if (icon is not null)
                    Remove(icon.Key);
                break;
            case NIM_SETVERSION:
                if (icon is not null)
                    icon.Version = nid->uTimeoutOrVersion;
                break;
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

    private const uint WM_CONTEXTMENU = 0x007B, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, NIN_SELECT = 0x0400;

    public void Click(TrayIcon icon, int button, (int X, int Y)? at)
    {
        if (!IsWindow(icon.Window) || icon.CallbackMessage == 0)
            return;
        var point = at ?? CursorPosition();
        // Apps read the cursor when they open a menu, and ignore the point in the message.
        if (at is { } target)
            PlaceCursor(target);
        // A menu only appears if the app may take the foreground. A programmatic click has not just
        // received input, so join the foreground thread long enough to hand that right over.
        LetAppTakeForeground(icon);
        switch (button)
        {
            case 0:
                Send(icon, WM_LBUTTONDOWN, point);
                Send(icon, WM_LBUTTONUP, point);
                if (icon.Version >= TrayProtocol.Version)
                    Send(icon, NIN_SELECT, point);
                break;
            case 1:
                Send(icon, WM_RBUTTONDOWN, point);
                Send(icon, WM_RBUTTONUP, point);
                if (icon.Version >= TrayProtocol.Version)
                    Send(icon, WM_CONTEXTMENU, point);
                break;
            default:
                Send(icon, WM_MBUTTONDOWN, point);
                Send(icon, WM_MBUTTONUP, point);
                break;
        }
    }

    private void LetAppTakeForeground(TrayIcon icon)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var thread = GetCurrentThreadId();
        var attached = foregroundThread != thread && AttachThreadInput(foregroundThread, thread, true);
        try
        {
            SetForegroundWindow(icon.Window);
            AllowSetForegroundWindow((uint)icon.ProcessId);
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
        _cursorRestore = _platform.Loop.Schedule(TimeSpan.FromMilliseconds(400), () => SetCursorPos(previous.X, previous.Y));
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

    /// <summary>Draws an icon into a straight-alpha image. Icons without alpha take it from their mask.</summary>
    private static SKImage? IconImage(nint icon)
    {
        if (icon == 0 || !GetIconInfo(icon, out var info))
            return null;
        try
        {
            var size = 32;
            if (GetObjectW(info.hbmColor != 0 ? info.hbmColor : info.hbmMask, sizeof(BITMAP), out var bmp) != 0)
                size = Math.Clamp(bmp.bmWidth, 16, 256);
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
    private const uint DI_MASK = 0x1, DI_NORMAL = 0x3;
    private const uint LwaAlpha = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public nuint dwData;
        public uint cbData;
        public void* lpData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA32
    {
        public uint cbSize;
        public uint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public uint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public uint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHELLTRAYDATA
    {
        public int dwSignature;
        public uint dwMessage;
        public NOTIFYICONDATA32 nid;
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
