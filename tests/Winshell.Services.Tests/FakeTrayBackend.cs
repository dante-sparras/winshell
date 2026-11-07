using Winshell.Native;
using Winshell.Tray;

namespace Winshell.Services.Tests;

/// <summary>
/// The notification area without a window, a session owner or Explorer. A test plays the apps, Explorer and Windows'
/// settings, and reads what the tray asked of Windows.
/// </summary>
internal sealed class FakeTrayBackend : ITrayBackend
{
    private Func<NotifyIconCall, bool>? _apply;

    public event Action? TaskbarCreated;

    public event Action? ExplorerInFront;

    public event Action? SettingsChanged;

    /// <summary>What the tray asked of Windows, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The callback messages sent to apps, in order; each is a <c>notify</c> in <see cref="Calls"/>.</summary>
    public List<(nint Window, uint Message, nint WParam, nint LParam)> Notified { get; } = [];

    /// <summary>Another instance has the notification area.</summary>
    public bool Taken { get; set; }

    public bool IsOpen => _apply is not null;

    public nint Explorer { get; set; } = 0x500;

    public (int X, int Y) Cursor { get; set; }

    public List<TrayIconSetting> Settings { get; } = [];

    /// <summary>The apps' windows that are still there, and the processes that own them.</summary>
    public Dictionary<nint, uint> Windows { get; } = [];

    /// <summary>The programs behind the apps' windows.</summary>
    public Dictionary<nint, string> Programs { get; } = [];

    /// <summary>Integrity levels by process; any other process is at medium.</summary>
    public Dictionary<uint, uint> Integrities { get; } = [];

    /// <summary>What an icon handle draws.</summary>
    public Dictionary<nint, PixelImage> Images { get; } = [];

    /// <summary>The resource an icon handle was loaded from; any other was drawn by its app.</summary>
    public Dictionary<nint, IconResource> Resources { get; } = [];

    /// <summary>What a resource draws at a size; any other size can't be drawn.</summary>
    public Dictionary<(IconResource Resource, int Size), PixelImage> Drawn { get; } = [];

    /// <summary>An app's <c>Shell_NotifyIcon</c> call, and the tray's answer.</summary>
    public bool Send(NotifyIconCall call)
    {
        return (_apply ?? throw new InvalidOperationException("The tray has not opened its window."))(call);
    }

    public void SignalTaskbarCreated()
    {
        TaskbarCreated?.Invoke();
    }

    public void SignalExplorerInFront()
    {
        ExplorerInFront?.Invoke();
    }

    public void SignalSettingsChanged()
    {
        SettingsChanged?.Invoke();
    }

    public bool TryOwn()
    {
        return !Taken;
    }

    public bool Open(Func<NotifyIconCall, bool> apply)
    {
        Calls.Add("open");
        _apply = apply;
        return true;
    }

    public void Close()
    {
        Calls.Add("close");
        _apply = null;
    }

    public void Dispose()
    {
        Calls.Add("dispose");
    }

    public void Raise()
    {
        Calls.Add("raise");
    }

    public bool Lower()
    {
        Calls.Add("lower");
        return IsOpen && Explorer != 0;
    }

    public void Broadcast()
    {
        Calls.Add("broadcast");
    }

    public IReadOnlyList<TrayIconSetting> ReadSettings()
    {
        return [.. Settings];
    }

    public (PixelImage? Image, IconResource? Resource) ReadIcon(nint icon)
    {
        return (Images.GetValueOrDefault(icon), Resources.GetValueOrDefault(icon));
    }

    public PixelImage? DrawIcon(IconResource resource, int size)
    {
        return Drawn.GetValueOrDefault((resource, size));
    }

    public uint ProcessOf(nint window)
    {
        return Windows.GetValueOrDefault(window);
    }

    public string ProgramOf(nint window)
    {
        return Programs.GetValueOrDefault(window, "");
    }

    public uint? IntegrityOf(uint processId)
    {
        return Integrities.TryGetValue(processId, out var level) ? level : ProcessIntegrity.Medium;
    }

    public void MoveCursor((int X, int Y) point)
    {
        Calls.Add($"move {point.X},{point.Y}");
    }

    public void LetAppTakeForeground(nint window, uint processId)
    {
        Calls.Add($"foreground {window:x} {processId}");
    }

    public void Notify(nint window, uint message, nint wParam, nint lParam)
    {
        Calls.Add("notify");
        Notified.Add((window, message, wParam, lParam));
    }
}
