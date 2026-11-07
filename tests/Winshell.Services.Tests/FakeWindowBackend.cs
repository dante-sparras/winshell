using Winshell.Native;
using Winshell.Windows;

namespace Winshell.Services.Tests;

/// <summary>Windows a test opens, changes and closes by hand, raising the events Windows would. No real window is read or moved.</summary>
internal sealed class FakeWindowBackend : IWindowBackend
{
    /// <summary>The windows that exist, in z-order, with what reading each gives.</summary>
    public Dictionary<nint, WindowRead> Shown { get; } = [];

    /// <summary>What Windows says about each window the taskbar's rule uses.</summary>
    public Dictionary<nint, TaskbarWindowFacts> Facts { get; } = [];

    public Dictionary<nint, nint> Owners { get; } = [];

    public Dictionary<string, ScreenInfo> Screens { get; } = [];

    public Dictionary<nint, string> DesktopIds { get; } = [];

    public List<string> Calls { get; } = [];

    public nint Foreground { get; set; }

    /// <summary>The monitors the foreground window covers.</summary>
    public HashSet<string> Fullscreen { get; } = [];

    /// <summary>Runs inside each read, with the window being read, as a message pumped during the read would.</summary>
    public Action<nint>? Reading { get; set; }

    public bool HasListeners => WinEvent is not null || Flashed is not null;

    public event Action<uint, nint, int, int>? WinEvent;

    public event Action<nint>? Flashed;

    /// <summary>The facts of a normal window with a title: the taskbar lists it.</summary>
    public static TaskbarWindowFacts Normal { get; } =
        new(true, true, false, false, false, false, false, false, true, false);

    public static WindowRead Window(string title = "Title")
    {
        return new WindowRead(title, false, false, false, "M1", "", "", @"C:\app.exe");
    }

    public void Show(nint hwnd, string title = "Title")
    {
        Show(hwnd, Window(title));
    }

    /// <summary>Shows a window, or changes one, and says its name changed.</summary>
    public void Show(nint hwnd, WindowRead read, TaskbarWindowFacts? facts = null)
    {
        Shown[hwnd] = read;
        Facts[hwnd] = facts ?? Normal;
        Raise(Win32.EVENT_OBJECT_NAMECHANGE, hwnd);
    }

    public void Destroy(nint hwnd)
    {
        Shown.Remove(hwnd);
        Facts.Remove(hwnd);
        Raise(Win32.EVENT_OBJECT_DESTROY, hwnd);
    }

    public void Focus(nint hwnd)
    {
        Foreground = hwnd;
        Raise(Win32.EVENT_SYSTEM_FOREGROUND, hwnd);
    }

    /// <summary>Moves or resizes a window.</summary>
    public void Move(nint hwnd)
    {
        Raise(Win32.EVENT_OBJECT_LOCATIONCHANGE, hwnd);
    }

    public void Flash(nint hwnd)
    {
        Flashed?.Invoke(hwnd);
    }

    private void Raise(uint eventType, nint hwnd)
    {
        WinEvent?.Invoke(eventType, hwnd, Win32.OBJID_WINDOW, Win32.CHILDID_SELF);
    }

    public void Start(Log log)
    {
    }

    public void WatchForeground(Log log)
    {
    }

    public IReadOnlySet<string> FullscreenScreens()
    {
        return Fullscreen.ToHashSet();
    }

    public IReadOnlyList<nint> TopLevel()
    {
        return [.. Shown.Keys];
    }

    public nint OwnerOf(nint hwnd)
    {
        return Owners.GetValueOrDefault(hwnd);
    }

    public nint RootOf(nint hwnd)
    {
        return hwnd;
    }

    public TaskbarWindowFacts TaskbarFacts(nint hwnd)
    {
        return Facts.GetValueOrDefault(hwnd);
    }

    /// <summary>The window, with the package id and program path left out when they were not asked for.</summary>
    public WindowRead Read(nint hwnd, bool package, bool executable)
    {
        Reading?.Invoke(hwnd);
        var read = Shown[hwnd];
        return read with
        {
            PackageAppId = package && read.AppUserModelId.Length == 0 ? read.PackageAppId : null,
            Executable = executable ? read.Executable : null
        };
    }

    public ScreenInfo? Screen(string deviceName)
    {
        return Screens.GetValueOrDefault(deviceName);
    }

    public string DesktopId(nint hwnd)
    {
        return DesktopIds.GetValueOrDefault(hwnd, "");
    }

    public bool MoveToDesktop(nint hwnd, Guid desktop)
    {
        DesktopIds[hwnd] = desktop.ToString("D");
        return true;
    }

    public void Activate(nint hwnd)
    {
        Calls.Add($"activate:{hwnd}");
    }

    public void Close(nint hwnd)
    {
        Calls.Add($"close:{hwnd}");
    }

    public void SetMaximized(nint hwnd, bool maximized)
    {
        Calls.Add($"maximize:{hwnd}:{maximized}");
    }

    public void SetMinimized(nint hwnd, bool minimized)
    {
        Calls.Add($"minimize:{hwnd}:{minimized}");
    }

    public void MoveToMonitor(nint hwnd, string deviceName)
    {
        Calls.Add($"move:{hwnd}:{deviceName}");
    }
}
