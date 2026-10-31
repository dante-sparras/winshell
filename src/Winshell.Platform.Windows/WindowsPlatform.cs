using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.Input;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows;

/// <summary>Winshell on Windows: monitors, windows, the clipboard, the theme and the system's notices.</summary>
public sealed unsafe class WindowsPlatform : IShellPlatform, IClipboard, IPlatformServices, IDisposable
{
    private readonly List<Win32Window> _windows = [];
    private readonly HashSet<Win32Window> _grabbing = [];
    private List<ScreenInfo> _screens = [];
    private nint _mouseHook;
    private IDisposable? _relayout;
    private IDisposable? _screenRefresh;
    private Services.Tray.TrayHost? _tray;
    private static WindowsPlatform? _current;

    public WindowsPlatform(WinEventLoop loop, Log log)
    {
        Loop = loop;
        Log = log;
        Hotkeys = new WindowsHotkeyRegistrar(loop);
        _current = this;
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        _screens = Platform.Windows.Screens.Enumerate().Select(m => m.Info).ToList();
        ReadTheme();
        ListenForClipboard();
        SystemEvents.DisplaySettingsChanged += (_, _) => Loop.Post(RefreshScreens);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
                Loop.Post(ReadTheme);
        };
    }

    public WinEventLoop Loop { get; }

    public Log Log { get; }

    /// <summary>Global hotkeys for this process. Disposing the platform unregisters them and removes the keyboard hook.</summary>
    internal WindowsHotkeyRegistrar Hotkeys { get; }

    public IReadOnlyList<ScreenInfo> Screens => _screens;

    public event Action? ScreensChanged;

    /// <summary>The notification area host, started the first time a shell uses the system tray.</summary>
    internal Services.Tray.TrayHost Tray
    {
        get
        {
            if (_tray is null)
            {
                _tray = new Services.Tray.TrayHost(Loop, Log, Services.Tray.TrayNames.Session, Services.Tray.TrayHost.BroadcastTaskbarCreated);
                Services.Tray.TrayIconProvider.Host = _tray;
                _tray.Start();
            }
            return _tray;
        }
    }

    /// <summary>The native windows that exist now.</summary>
    internal IReadOnlyList<Win32Window> Windows => _windows;

    public string PrimaryScreenId => _screens.FirstOrDefault(s => s.IsPrimary)?.Id ?? _screens.FirstOrDefault()?.Id ?? "";

    public IWindowBackend CreateWindow(WindowKind kind, IWindowClient client)
    {
        var window = new Win32Window(this, kind, client);
        _windows.Add(window);
        return window;
    }

    internal void Forget(Win32Window window)
    {
        _windows.Remove(window);
        _grabbing.Remove(window);
        if (_grabbing.Count == 0)
            Unhook();
    }

    // Screens and the work area

    private void RefreshScreens()
    {
        var next = Platform.Windows.Screens.Enumerate().Select(m => m.Info).ToList();
        if (next.SequenceEqual(_screens))
            return;
        _screens = next;
        ScreensChanged?.Invoke();
        WorkAreaMayHaveChanged();
    }

    internal void SystemChanged(uint message, nint wParam)
    {
        if (message == Win32.WM_DISPLAYCHANGE)
        {
            ScreensMayHaveChanged();
        }
        else if (message == Win32.WM_SETTINGCHANGE)
        {
            TextRaster.Refresh();
            // The taskbar moved, resized, or started or stopped hiding.
            if (wParam == Win32.SPI_SETWORKAREA)
                WorkAreaMayHaveChanged();
            else
                ScreensMayHaveChanged();
            Loop.Post(ReadTheme);
        }
        else
        {
            Loop.Post(ReadTheme);
        }
    }

    /// <summary>
    /// Reads the monitors again soon, once a burst of display messages settles: a monitor was plugged in or out, or
    /// its resolution or scale changed. Panels are placed again only if something changed.
    /// </summary>
    internal void ScreensMayHaveChanged()
    {
        _screenRefresh?.Dispose();
        _screenRefresh = Loop.Schedule(TimeSpan.FromMilliseconds(200), RefreshScreens);
    }

    /// <summary>
    /// Places every panel again soon. Explorer moves the work area some time after an app bar changes, and
    /// panels that respect other bars' zones follow it.
    /// </summary>
    internal void WorkAreaMayHaveChanged()
    {
        _relayout?.Dispose();
        _relayout = Loop.Schedule(TimeSpan.FromMilliseconds(50), () =>
        {
            _relayout = null;
            RefreshScreens();
            foreach (var w in _windows.ToArray())
            {
                if (w.Kind == WindowKind.Panel && w.IsVisible)
                    w.PlacePanel();
            }
        });
    }

    // Popups that close on a click outside them

    internal void UpdatePopupGrab(Win32Window window, bool grabbing)
    {
        if (grabbing && window.Handle != 0)
            _grabbing.Add(window);
        else
            _grabbing.Remove(window);
        if (_grabbing.Count > 0 && _mouseHook == 0)
            _mouseHook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, &MouseHook, Win32.GetModuleHandleW(null), 0);
        else if (_grabbing.Count == 0)
            Unhook();
    }

    private void Unhook()
    {
        if (_mouseHook == 0)
            return;
        Win32.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
    }

    [UnmanagedCallersOnly]
    private static nint MouseHook(int code, nint wParam, nint lParam)
    {
        var platform = _current;
        if (code >= 0 && platform is not null)
        {
            var msg = (uint)wParam;
            if (msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or Win32.WM_XBUTTONDOWN)
            {
                var info = (MSLLHOOKSTRUCT*)lParam;
                var pt = info->pt;
                NativeCallback.Guard(platform.Log, "shell", () =>
                {
                    foreach (var w in platform._grabbing.ToArray())
                    {
                        if (!w.ContainsScreenPoint(pt))
                            platform.Loop.Post(w.Dismiss);
                    }
                });
            }
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    // Theme

    private void ReadTheme()
    {
        var dark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = WindowsModeDark(key?.GetValue("SystemUsesLightTheme"), key?.GetValue("AppsUseLightTheme"));
        }
        catch (System.Security.SecurityException)
        {
        }
        var theme = Theme.Default(dark) with
        {
            System = ReadSystemColors(),
            IsHighContrast = HighContrastOn(),
            WheelScrollLines = WheelScrollLines(),
            CaretBlinkTime = Win32.GetCaretBlinkTime() is > 0 and var blink ? blink : 530,
            CaretTimeout = SystemParameter(Win32.SPI_GETCARETTIMEOUT, 5000),
            CaretWidth = SystemParameter(Win32.SPI_GETCARETWIDTH, 1),
            DragThreshold = Math.Max(Metric(Win32.SM_CXDRAG, 4), Metric(Win32.SM_CYDRAG, 4)),
            DoubleClickTime = Win32.GetDoubleClickTime() is > 0 and var time ? time : 500,
            DoubleClickWidth = Metric(Win32.SM_CXDOUBLECLK, 4),
            DoubleClickHeight = Metric(Win32.SM_CYDOUBLECLK, 4),
        };
        var animations = true;
        var autoHideScrollBars = true;
        try
        {
            var settings = new global::Windows.UI.ViewManagement.UISettings();
            animations = settings.AnimationsEnabled;
            autoHideScrollBars = settings.AutoHideScrollBars;
            QColor Get(global::Windows.UI.ViewManagement.UIColorType type)
            {
                var c = settings.GetColorValue(type);
                return QColor.FromRgb(c.R, c.G, c.B, c.A);
            }
            theme = theme with
            {
                Accents = new AccentColors(
                    Get(global::Windows.UI.ViewManagement.UIColorType.Accent),
                    Get(global::Windows.UI.ViewManagement.UIColorType.AccentLight1),
                    Get(global::Windows.UI.ViewManagement.UIColorType.AccentLight2),
                    Get(global::Windows.UI.ViewManagement.UIColorType.AccentLight3),
                    Get(global::Windows.UI.ViewManagement.UIColorType.AccentDark1),
                    Get(global::Windows.UI.ViewManagement.UIColorType.AccentDark2),
                    Get(global::Windows.UI.ViewManagement.UIColorType.AccentDark3)),
                AnimationsEnabled = animations,
                AutoHideScrollBars = autoHideScrollBars,
            };
        }
        catch (COMException)
        {
            theme = theme with { AnimationsEnabled = animations, AutoHideScrollBars = autoHideScrollBars };
        }
        Theme.Current = theme;
    }

    /// <summary>
    /// Whether Windows mode is dark, from the Personalize values, where <c>0</c> is dark. The taskbar and Start follow
    /// Windows mode, so every Winshell window does. A missing Windows-mode value follows app mode, and a missing pair is
    /// light.
    /// </summary>
    internal static bool WindowsModeDark(object? systemValue, object? appsValue) =>
        systemValue is int systemLight ? systemLight == 0 : appsValue is int appsLight && appsLight == 0;

    private static bool HighContrastOn()
    {
        var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return Win32.SystemParametersInfoW(Win32.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) && (hc.dwFlags & Win32.HCF_HIGHCONTRASTON) != 0;
    }

    private static uint WheelScrollLines()
    {
        uint lines;
        return Win32.SystemParametersInfoW(Win32.SPI_GETWHEELSCROLLLINES, 0, &lines, 0) ? lines : 3;
    }

    /// <summary>A <c>DWORD</c> system parameter, or <paramref name="fallback"/> when Windows has none.</summary>
    private static uint SystemParameter(uint action, uint fallback)
    {
        uint value;
        return Win32.SystemParametersInfoW(action, 0, &value, 0) ? value : fallback;
    }

    /// <summary>A system metric in logical pixels, or <paramref name="fallback"/> when Windows has none.</summary>
    private static double Metric(int index, double fallback) =>
        Win32.GetSystemMetricsForDpi(index, 96) is > 0 and var value ? value : fallback;

    private static SystemColors ReadSystemColors()
    {
        static QColor Get(int index)
        {
            var c = Win32.GetSysColor(index);
            return QColor.FromRgb((byte)c, (byte)(c >> 8), (byte)(c >> 16));
        }
        return new SystemColors(
            Get(Win32.COLOR_WINDOW),
            Get(Win32.COLOR_WINDOWTEXT),
            Get(Win32.COLOR_HIGHLIGHT),
            Get(Win32.COLOR_HIGHLIGHTTEXT),
            Get(Win32.COLOR_HOTLIGHT),
            Get(Win32.COLOR_GRAYTEXT),
            Get(Win32.COLOR_BTNFACE),
            Get(Win32.COLOR_BTNTEXT));
    }

    // Clipboard

    public event Action? Changed;

    private nint _clipboardWindow;
    private string? _clipboardText;
    private const string ClipboardClass = "WinshellClipboard";

    /// <summary>A message-only window, so another program copying text updates <c>Shell.clipboardText</c>.</summary>
    private void ListenForClipboard()
    {
        fixed (char* name = ClipboardClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &ClipboardProc,
                hInstance = Win32.GetModuleHandleW(null),
                lpszClassName = name,
            };
            if (Win32.RegisterClassExW(wc) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                Log.Warn("shell", "Could not listen for clipboard changes");
                return;
            }
        }
        _clipboardWindow = Win32.CreateWindowExW(0, ClipboardClass, "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, Win32.GetModuleHandleW(null), 0);
        if (_clipboardWindow == 0 || !Win32.AddClipboardFormatListener(_clipboardWindow))
        {
            Log.Warn("shell", "Could not listen for clipboard changes");
            if (_clipboardWindow != 0)
                Win32.DestroyWindow(_clipboardWindow);
            _clipboardWindow = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static nint ClipboardProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE && _current is { } platform && hwnd == platform._clipboardWindow)
            NativeCallback.Guard(platform.Log, "shell", () => platform.Loop.Post(platform.OnClipboardChanged));
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void OnClipboardChanged()
    {
        var text = GetText();
        if (text == _clipboardText)
            return;
        _clipboardText = text;
        Changed?.Invoke();
    }

    public string? GetText()
    {
        if (!Win32.OpenClipboard(0))
            return null;
        try
        {
            var handle = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
            if (handle == 0)
                return null;
            var ptr = Win32.GlobalLock(handle);
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Win32.GlobalUnlock(handle);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    public void SetText(string text)
    {
        if (!Win32.OpenClipboard(0))
            return;
        try
        {
            Win32.EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var mem = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)bytes);
            var ptr = Win32.GlobalLock(mem);
            fixed (char* src = text)
                Buffer.MemoryCopy(src, (void*)ptr, bytes, text.Length * 2);
            ((char*)ptr)[text.Length] = '\0';
            Win32.GlobalUnlock(mem);
            Win32.SetClipboardData(Win32.CF_UNICODETEXT, mem);
            _clipboardText = text;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    // Platform services

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn("platform", $"Could not open {url}: {e.Message}");
        }
    }

    public void Quit(int code) => Loop.Quit(code);

    public void Dispose()
    {
        Hotkeys.Dispose();
        // The next instance takes the notification area over, and apps register with it.
        if (_tray is not null)
        {
            Services.Tray.TrayIconProvider.Host = null;
            _tray.Dispose();
            _tray = null;
        }
        Unhook();
        if (_clipboardWindow != 0)
        {
            Win32.RemoveClipboardFormatListener(_clipboardWindow);
            Win32.DestroyWindow(_clipboardWindow);
            _clipboardWindow = 0;
        }
        foreach (var w in _windows.ToArray())
            w.Dispose();
    }
}
