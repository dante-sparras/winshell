using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows;

/// <summary>
/// Keeps a session lock from being got around while it is up. It swallows the shortcuts that leave the lock
/// screen, keeps the lock windows in front, and locks Windows itself when the session could be reached anyway:
/// Winshell exiting or crashing while locked, or Ctrl+Alt+Del, whose screen offers Task Manager.
/// </summary>
internal static unsafe partial class SessionLockGuard
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint LLKHF_ALTDOWN = 0x20;
    private const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_CONTROL = 0x11, VK_APPS = 0x5D;

    private static WindowsPlatform? _platform;
    private static nint _keyboardHook;
    private static nint _desktopHook;
    private static IDisposable? _raiseTimer;
    private static bool _exitHandlersAdded;

    public static bool IsLocked { get; private set; }

    public static void Set(WindowsPlatform platform, bool locked)
    {
        if (locked == IsLocked)
            return;
        IsLocked = locked;
        _platform = platform;
        if (locked)
        {
            AddExitHandlers();
            _keyboardHook = Win32.SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardHook, Win32.GetModuleHandleW(null), 0);
            _desktopHook = SetWinEventHook(EVENT_SYSTEM_DESKTOPSWITCH, EVENT_SYSTEM_DESKTOPSWITCH, 0, &DesktopSwitched, 0, 0, 0);
            KeepInFront();
        }
        else
        {
            if (_keyboardHook != 0)
                Win32.UnhookWindowsHookEx(_keyboardHook);
            if (_desktopHook != 0)
                UnhookWinEvent(_desktopHook);
            _keyboardHook = _desktopHook = 0;
            _raiseTimer?.Dispose();
            _raiseTimer = null;
        }
    }

    /// <summary>Puts the lock windows back in front every half second, over windows that made themselves topmost.</summary>
    private static void KeepInFront()
    {
        if (!IsLocked || _platform is null)
            return;
        _platform.RaiseLockWindows();
        _raiseTimer = _platform.Loop.Schedule(TimeSpan.FromMilliseconds(500), KeepInFront);
    }

    private static void AddExitHandlers()
    {
        if (_exitHandlersAdded)
            return;
        _exitHandlersAdded = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => LockIfLocked();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => LockIfLocked();
    }

    private static void LockIfLocked()
    {
        if (IsLocked)
            LockWorkStation();
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && IsLocked)
        {
            var info = (KBDLLHOOKSTRUCT*)lParam;
            var key = (int)info->vkCode;
            var alt = (info->flags & LLKHF_ALTDOWN) != 0;
            var ctrl = GetAsyncKeyState(VK_CONTROL) < 0;
            var blocked = key is VK_LWIN or VK_RWIN or VK_APPS
                || (alt && key is VK_TAB or VK_ESCAPE or VK_F4)
                || (ctrl && key == VK_ESCAPE);
            if (blocked)
                return 1;
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static void DesktopSwitched(nint hook, uint ev, nint hwnd, int obj, int child, uint thread, uint time)
    {
        // Ctrl+Alt+Del moved to the secure desktop, which offers Task Manager: hand the lock to Windows.
        if (IsLocked)
            _platform?.Loop.Post(() => LockWorkStation());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);
}
