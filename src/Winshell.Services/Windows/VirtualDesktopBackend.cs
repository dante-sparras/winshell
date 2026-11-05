using System.Globalization;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>Reads and changes virtual desktops. Tests use a stand-in; the shell uses <see cref="VirtualDesktopBackend"/>.</summary>
internal interface IVirtualDesktopBackend
{
    DesktopContract Contract { get; }

    /// <summary>Windows switched virtual desktop. Raised on the thread that first called <see cref="StartWatching"/>.</summary>
    event Action? Switched;

    /// <summary>Starts raising <see cref="Switched"/>. False when Windows will not report a switch.</summary>
    bool StartWatching();

    /// <summary>Changes when a desktop is added or removed. Reading it makes no call into Explorer.</summary>
    string ListStamp { get; }

    bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error);

    bool SwitchTo(Guid id);

    Guid? Create();

    bool Remove(Guid id, Guid fallback);
}

/// <summary>The build Windows reports, from <c>RtlGetVersion</c> rather than the version a manifest can change.</summary>
internal readonly record struct WindowsVersion(uint Build, uint Update)
{
    public override string ToString() => Update == 0 ? Build.ToString(CultureInfo.InvariantCulture) : $"{Build}.{Update}";

    public static WindowsVersion Current => _current ??= Read();

    private static WindowsVersion? _current;

    public static WindowsVersion Read() => OsVersion.Build() is { } build ? new(build, ReadUpdate()) : new(0, 0);

    private static uint ReadUpdate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("UBR") switch
            {
                int value when value > 0 => (uint)value,
                uint value => value,
                _ => 0,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }
}

/// <summary>
/// The virtual desktops through Explorer's internal manager, on a known build. The internal table gained
/// <c>SwitchDesktopAndMoveForegroundView</c> in Windows 11 24H2 (build 26100); 23H2 uses the same interface id and
/// different slots, so <see cref="DesktopContract"/> refuses every other build. A failed read never calls create,
/// switch or remove. Which desktop a window is on is <see cref="WindowDesktop"/>'s. The switch hook stays for the
/// process, the same way the window list's hooks do.
/// </summary>
internal sealed class VirtualDesktopBackend : IVirtualDesktopBackend
{
    public static VirtualDesktopBackend Shared { get; } = new();

    private WinEventHook? _hook;

    private VirtualDesktopBackend()
    {
    }

    public event Action? Switched;

    public DesktopContract Contract
    {
        get
        {
            var version = WindowsVersion.Current;
            var contract = DesktopContract.ForBuild(version.Build);
            if (contract.Supported)
                return contract;
            return new(false,
                $"Windows {version} is outside the virtual desktop method table Winshell knows ({DesktopContract.OldestBuild} through {DesktopContract.NewestBuild}).");
        }
    }

    public bool StartWatching()
    {
        _hook ??= WinEventHook.Start(Win32.EVENT_SYSTEM_DESKTOPSWITCH, OnEvent);
        return _hook is not null;
    }

    private void OnEvent(uint eventType, nint hwnd, int idObject, int idChild) =>
        NativeCallback.Guard(Log.Default, "desktops", () => Switched?.Invoke());

    public string ListStamp =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops", "VirtualDesktopIDs", null) is byte[] ids
            ? Convert.ToHexString(ids)
            : "";

    public bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error)
    {
        desktops = [];
        if (!ImmersiveDesktops.TryRead(out var entries, out current, out error))
            return false;
        desktops = entries.Select(entry => new DesktopRecord(entry.Id, entry.Name)).ToList();
        return true;
    }

    public bool SwitchTo(Guid id)
    {
        if (ImmersiveDesktops.SwitchTo(id, out var problem))
            return true;
        Log.Default.Warn("desktops", $"SwitchDesktop {problem}.");
        return false;
    }

    public Guid? Create()
    {
        if (ImmersiveDesktops.Create(out var problem) is { } id)
            return id;
        Log.Default.Warn("desktops", $"CreateDesktop {problem}.");
        return null;
    }

    public bool Remove(Guid id, Guid fallback)
    {
        if (id == Guid.Empty || fallback == Guid.Empty || id == fallback)
            return false;
        if (ImmersiveDesktops.Remove(id, fallback, out var problem))
            return true;
        Log.Default.Warn("desktops", $"RemoveDesktop {problem}.");
        return false;
    }
}
