using System.Runtime.InteropServices;
using Winshell.Core.Diagnostics;

namespace Winshell;

/// <summary>Win32 the shell's own windows need. The copies in Winland and Winoma stay for the windows that are not a shell surface.</summary>
internal static class ShellNative
{
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;
    internal const int SwHide = 0;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint RdwInvalidate = 0x0001;
    internal const uint WmWindowPosChanging = 0x0046;
    internal const int DwmwaCloak = 13;
    internal const int DwmwaUseImmersiveDarkMode = 20;
    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaCaptionColor = 35;
    internal const int DwmwaVisibleFrameBorderThickness = 37;

    internal delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [DllImport("user32.dll")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool RedrawWindow(nint hwnd, nint update, nint region, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(nint hwnd, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("comctl32.dll", SetLastError = true)]
    internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    internal static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool OpenClipboard(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    internal static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll")]
    internal static extern bool CloseClipboard();

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    internal static extern bool GlobalUnlock(nint memory);

    internal const uint GmemMoveable = 0x0002;
    internal const uint CfUnicodeText = 13;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}

/// <summary>A window's screen rectangle in physical pixels. The right and bottom edges are outside it.</summary>
internal readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>One monitor, in physical pixels.</summary>
internal readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>Reads the pointer, a key, or a window rectangle at the moment it is asked.</summary>
internal static class PointerProbe
{
    public const int LeftButton = 0x01;
    public const int RightButton = 0x02;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Alt = 0x12;

    public static bool TryCursor(out int x, out int y)
    {
        var read = ShellNative.GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
        return read;
    }

    public static bool IsDown(int virtualKey) => (ShellNative.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static bool TryWindow(nint hwnd, out WindowBounds bounds)
    {
        if (!ShellNative.GetWindowRect(hwnd, out var rect))
        {
            bounds = default;
            return false;
        }

        bounds = new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }
}

/// <summary>Monitor rectangles and DPI conversion for the shell's own windows.</summary>
internal static class ScreenMetrics
{
    private static readonly bool AsTwo = File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "winoma", "two-monitors"));

    public static ScreenBounds Of(nint window)
    {
        var monitor = ShellNative.MonitorFromWindow(window, 2);
        var info = new ShellNative.MonitorInfo { Size = Marshal.SizeOf<ShellNative.MonitorInfo>() };
        if (monitor == 0 || !ShellNative.GetMonitorInfo(monitor, ref info))
        {
            return new ScreenBounds(0, 0, 1920, 1080);
        }

        var screen = info.Monitor;
        if (!AsTwo)
        {
            return Box(screen.Left, screen.Top, screen.Right, screen.Bottom);
        }

        var whole = new ShellRect(screen.Left, screen.Top, Math.Max(1, screen.Right - screen.Left), Math.Max(1, screen.Bottom - screen.Top));
        var at = ShellNative.GetWindowRect(window, out var rect)
            ? new ShellRect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top))
            : whole;
        var half = Half(whole, at);
        return new ScreenBounds(half.X, half.Y, half.Width, half.Height);
    }

    public static double Scale(nint window)
    {
        var dpi = ShellNative.GetDpiForWindow(window);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    public static int Dips(nint window, int dips)
    {
        var dpi = ShellNative.GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Ceiling(dips * dpi / 96.0);
    }

    public static void Invalidate()
    {
    }

    private static ScreenBounds Box(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));

    private static ShellRect Half(ShellRect screen, ShellRect window)
    {
        var leftWidth = screen.Width / 2;
        var left = new ShellRect(screen.X, screen.Y, leftWidth, screen.Height);
        var right = new ShellRect(screen.X + leftWidth, screen.Y, screen.Width - leftWidth, screen.Height);
        var onLeft = Across(window, left);
        var onRight = Across(window, right);
        if (onLeft == 0 && onRight == 0)
        {
            return (long)window.X + (window.Width / 2) >= right.X ? right : left;
        }

        return onRight > onLeft ? right : left;
    }

    private static long Across(ShellRect window, ShellRect half) =>
        Math.Max(0, Math.Min((long)window.X + window.Width, (long)half.X + half.Width) - Math.Max(window.X, half.X));
}

/// <summary>Strips the frame Windows would draw around the shell's own windows.</summary>
internal static class BorderlessFrame
{
    public const int None = unchecked((int)0xFFFFFFFE);
    private const int SystemOutline = unchecked((int)0xFFFFFFFF);
    private const nuint FrameId = 1;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmPointerDown = 0x0246;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmSettingChange = 0x001A;
    private const nint SpiSetWorkArea = 0x002F;
    private const uint WmEndSession = 0x0016;

    private static readonly ShellNative.SubclassProc Proc = OnMessage;
    private static int _pressDepth;
    private static bool _light;
    private static readonly Dictionary<nint, (int Color, bool Light)> Quieted = [];

    public static event Action? Pressed;
    public static event Action? ScreenChanged;
    public static event Action? SessionEnding;
    public static event Action? SessionStayed;

    public static int VisiblePixels(nint window)
    {
        if (window == 0
            || ShellNative.DwmGetWindowAttribute(window, ShellNative.DwmwaVisibleFrameBorderThickness, out var edge, sizeof(int)) != 0
            || edge is <= 0 or > 8)
        {
            return 1;
        }

        return edge;
    }

    public static void UseTheme(bool light) => _light = light;

    public static void Quiet(nint window, int colorRef = 0x00141414, bool force = false)
    {
        if (window == 0)
        {
            return;
        }

        ShellNative.SetWindowSubclass(window, Proc, FrameId, 0);
        var extended = (int)ShellNative.GetWindowLongPtr(window, ShellNative.GwlExStyle);
        var desired = extended | ShellNative.WsExNoActivate;
        if (desired != extended)
        {
            ShellNative.SetWindowLongPtr(window, ShellNative.GwlExStyle, desired);
        }

        if (!force && desired == extended && Quieted.TryGetValue(window, out var given) && given == (colorRef, _light))
        {
            return;
        }

        var dark = _light ? 0 : 1;
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var square = 1;
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaWindowCornerPreference, ref square, sizeof(int));
        var color = colorRef;
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaBorderColor, ref color, sizeof(int));
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaCaptionColor, ref color, sizeof(int));
        var margins = new ShellNative.Margins();
        _ = ShellNative.DwmExtendFrameIntoClientArea(window, ref margins);
        ShellNative.SetWindowPos(window, new nint(-1), 0, 0, 0, 0, ShellNative.SwpNoActivate | ShellNative.SwpFrameChanged | ShellNative.SwpNoSize | ShellNative.SwpNoMove);
        Quieted[window] = (colorRef, _light);
    }

    public static void Round(nint window, bool light, bool outline = false) => Chrome(window, light, round: true, outline);

    public static void Flat(nint window, bool light) => Chrome(window, light, round: false, outline: false);

    public static void Under(nint panel, nint bar)
    {
        if (panel == 0 || bar == 0)
        {
            return;
        }

        ShellNative.SetWindowPos(panel, bar, 0, 0, 0, 0, ShellNative.SwpNoMove | ShellNative.SwpNoSize | ShellNative.SwpNoActivate);
    }

    private static void Chrome(nint window, bool light, bool round, bool outline)
    {
        if (window == 0)
        {
            return;
        }

        UseTheme(light);
        var dark = light ? 0 : 1;
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var preference = round ? 2 : 1;
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaWindowCornerPreference, ref preference, sizeof(int));
        var none = None;
        var border = outline ? SystemOutline : None;
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaBorderColor, ref border, sizeof(int));
        _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaCaptionColor, ref none, sizeof(int));
    }

    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmNcCalcSize && wParam != 0)
        {
            return 0;
        }

        if (message == WmMouseActivate)
        {
            return 3;
        }

        if (message == WmEndSession)
        {
            (wParam != 0 ? SessionEnding : SessionStayed)?.Invoke();
        }

        if (message is WmDisplayChange or WmDpiChanged || (message == WmSettingChange && wParam == SpiSetWorkArea))
        {
            ScreenMetrics.Invalidate();
            ScreenChanged?.Invoke();
        }

        var owned = false;
        if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmPointerDown && _pressDepth == 0)
        {
            _pressDepth = 1;
            owned = true;
        }

        try
        {
            if (owned)
            {
                Pressed?.Invoke();
            }

            if (message == WmNcDestroy)
            {
                ShellNative.RemoveWindowSubclass(hwnd, Proc, id);
                Quieted.Remove(hwnd);
            }

            return ShellNative.DefSubclassProc(hwnd, message, wParam, lParam);
        }
        finally
        {
            if (owned)
            {
                _pressDepth = 0;
            }
        }
    }
}

/// <summary>Keeps a panel directly under the bar.</summary>
internal static class PanelLayer
{
    private const nuint Id = 4;
    private static readonly ShellNative.SubclassProc Proc = OnMessage;
    private static nint _bar;

    public static void Pin(nint panel, nint bar)
    {
        if (panel == 0)
        {
            return;
        }

        _bar = bar;
        ShellNative.SetWindowSubclass(panel, Proc, Id, 0);
    }

    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == ShellNative.WmWindowPosChanging && _bar != 0 && lParam != 0)
        {
            var insertAfter = IntPtr.Size;
            var flagsAt = (IntPtr.Size * 2) + 16;
            Marshal.WriteIntPtr(lParam, insertAfter, _bar);
            var flags = (uint)Marshal.ReadInt32(lParam, flagsAt);
            Marshal.WriteInt32(lParam, flagsAt, (int)(flags & ~ShellNative.SwpNoZOrder));
        }

        if (message == 0x0082)
        {
            ShellNative.RemoveWindowSubclass(hwnd, Proc, id);
        }

        return ShellNative.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}

/// <summary>Keeps a shown window off the screen while it draws.</summary>
internal static class PanelVeil
{
    public static void Cover(nint window) => Cloak(window, 1);

    public static void Lift(nint window) => Cloak(window, 0);

    private static void Cloak(nint window, int cloak)
    {
        if (window != 0)
        {
            _ = ShellNative.DwmSetWindowAttribute(window, ShellNative.DwmwaCloak, ref cloak, sizeof(int));
        }
    }
}

/// <summary>Paints the part of a panel window that its rows have not been drawn on yet.</summary>
internal static class PanelBackfill
{
    private const nuint Id = 5;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmNcDestroy = 0x0082;
    private const uint RdwErase = 0x0004;
    private const uint RdwEraseNow = 0x0200;
    private static readonly ShellNative.SubclassProc Proc = OnMessage;
    private static nint _brush;
    private static int _color = -1;

    public static void Use(nint window, byte red, byte green, byte blue)
    {
        if (window == 0)
        {
            return;
        }

        var color = red | (green << 8) | (blue << 16);
        if (color != _color)
        {
            var old = _brush;
            _brush = CreateSolidBrush(color);
            _color = color;
            if (old != 0)
            {
                ShellNative.DeleteObject(old);
            }
        }

        ShellNative.SetWindowSubclass(window, Proc, Id, 0);
    }

    public static void Paint(nint window)
    {
        if (window != 0 && _brush != 0)
        {
            ShellNative.RedrawWindow(window, 0, 0, ShellNative.RdwInvalidate | RdwErase | RdwEraseNow);
        }
    }

    private static nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmEraseBackground && _brush != 0 && wParam != 0 && GetClientRect(hwnd, out var client))
        {
            FillRect(wParam, ref client, _brush);
            return 1;
        }

        if (message == WmNcDestroy)
        {
            ShellNative.RemoveWindowSubclass(hwnd, Proc, id);
        }

        return ShellNative.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(int color);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out ShellNative.Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint dc, ref ShellNative.Rect rect, nint brush);
}

/// <summary>A small topmost window that never takes focus, such as the plugin pill.</summary>
internal static class FloatingPill
{
    private const int SwShowNoActivate = 4;
    private static readonly nint Topmost = new(-1);

    public static void Prepare(nint window, bool light)
    {
        if (window == 0)
        {
            return;
        }

        var extended = (int)ShellNative.GetWindowLongPtr(window, ShellNative.GwlExStyle);
        var desired = extended | ShellNative.WsExNoActivate | ShellNative.WsExToolWindow;
        if (desired != extended)
        {
            ShellNative.SetWindowLongPtr(window, ShellNative.GwlExStyle, desired);
        }

        BorderlessFrame.Round(window, light);
    }

    public static void ShowAt(nint window, int x, int y, int width, int height)
    {
        if (window == 0)
        {
            return;
        }

        ShellNative.SetWindowPos(window, Topmost, x, y, width, height, ShellNative.SwpNoActivate);
        ShellNative.ShowWindow(window, SwShowNoActivate);
    }

    public static void MoveTo(nint window, int x, int y)
    {
        if (window != 0)
        {
            ShellNative.SetWindowPos(window, 0, x, y, 0, 0, ShellNative.SwpNoSize | ShellNative.SwpNoZOrder | ShellNative.SwpNoActivate);
        }
    }

    public static void Hide(nint window)
    {
        if (window != 0)
        {
            ShellNative.ShowWindow(window, ShellNative.SwHide);
        }
    }

    public static (int CenterX, int Bottom, double Scale) Anchor()
    {
        var foreground = ShellNative.GetForegroundWindow();
        var monitor = ShellNative.MonitorFromWindow(foreground, 2);
        var info = new ShellNative.MonitorInfo { Size = Marshal.SizeOf<ShellNative.MonitorInfo>() };
        if (monitor == 0 || !ShellNative.GetMonitorInfo(monitor, ref info))
        {
            return (960, 1040, 1);
        }

        var work = info.Work;
        var scale = ShellNative.GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        var part = ScreenMetrics.Of(foreground);
        var left = Math.Max(work.Left, part.X);
        var right = Math.Min(work.Right, part.X + part.Width);
        return ((left + right) / 2, work.Bottom, scale);
    }
}

/// <summary>Places text on the clipboard from the shell's UI thread.</summary>
internal static class ShellClipboard
{
    public static bool Text(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        var memory = ShellNative.GlobalAlloc(ShellNative.GmemMoveable, (nuint)bytes.Length);
        if (memory == 0)
        {
            return false;
        }

        var locked = ShellNative.GlobalLock(memory);
        if (locked == 0)
        {
            return false;
        }

        Marshal.Copy(bytes, 0, locked, bytes.Length);
        ShellNative.GlobalUnlock(memory);
        if (!ShellNative.OpenClipboard(0))
        {
            return false;
        }

        try
        {
            if (!ShellNative.EmptyClipboard())
            {
                return false;
            }

            return ShellNative.SetClipboardData(ShellNative.CfUnicodeText, memory) != 0;
        }
        finally
        {
            ShellNative.CloseClipboard();
        }
    }
}

/// <summary>Lets a task run on. A fault is a warning. A cancelled task writes nothing.</summary>
internal static class ShellTasks
{
    public static void Forget(this Task task, string area, string action)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        task.ContinueWith(
            static (done, state) =>
            {
                var (area, action) = ((string, string))state!;
                Log.Warning(area, action + " failed", done.Exception?.GetBaseException());
            },
            (area, action),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
