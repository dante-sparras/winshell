using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>The Win32 the shell does itself: tagging its windows, the taskbar, and the monitors.</summary>
internal static class ShellWin32
{
    private static int _released;
    private static bool _hidTaskbar;

    public static void HideTaskbar()
    {
        Taskbar(show: false);
        _hidTaskbar = true;
    }

    /// <summary>Shows the Windows taskbar. A shell whose setting does not hide it does this, including one that replaced a shell that had hidden it.</summary>
    public static void ShowTaskbar()
    {
        Taskbar(show: true);
        _hidTaskbar = false;
    }

    public static void GiveBack()
    {
        Taskbar(show: true);
        RestoreCursors();
    }

    /// <summary>Reloads the person's cursors. A shell that hid the pointer cannot do this after it has been killed.</summary>
    public static void RestoreCursors() => RestoreCursor();

    public static void Tag(nint hwnd) => WindowTag.Tag(hwnd);

    public static void Reassert()
    {
        if (_hidTaskbar)
        {
            Taskbar(show: false);
        }
    }

    public static void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        if (_hidTaskbar)
        {
            Taskbar(show: true);
            _hidTaskbar = false;
        }

        RestoreCursor();
    }

    public static int Scale(nint hwnd, int dips)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Round(dips * (dpi / 96.0));
    }

    public static IReadOnlyList<(int Index, ShellRect Bounds)> Monitors()
    {
        var found = new List<ShellRect>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var rect = info.Monitor;
                found.Add(new ShellRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
            }

            return true;
        }, 0);
        return found.Select((rect, index) => (index, rect)).ToList();
    }

    /// <summary>Seconds since the last keyboard or mouse input. Zero when Windows does not say.</summary>
    public static int IdleSeconds()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        // Both counts wrap at 49.7 days, and the unsigned difference is right across the wrap.
        return (int)(unchecked((uint)Environment.TickCount - info.Time) / 1000);
    }

    public static bool WinlandHeld() => Held(@"Local\Winland");

    public static bool ShellHeld() => Held(@"Local\Winshell");

    private static bool Held(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var held))
            {
                return false;
            }

            held.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static void Taskbar(bool show)
    {
        var command = show ? 5 : 0;
        Show("Shell_TrayWnd", command);
        Show("Shell_SecondaryTrayWnd", command);
    }

    private static void Show(string className, int command)
    {
        var hwnd = FindWindow(className, null);
        if (hwnd != 0)
        {
            ShowWindow(hwnd, command);
        }
    }

    private static void RestoreCursor() => SystemParametersInfo(0x0057, 0, 0, 0);

    private static class WindowTag
    {
        public static void Tag(nint hwnd)
        {
            if (hwnd != 0)
            {
                SetProp(hwnd, "Winshell.Window", 1);
            }
        }
    }

    private delegate bool MonitorProc(nint monitor, nint hdc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInput info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(nint hwnd, string name, nint value);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint param, nint value, uint winIni);

}
