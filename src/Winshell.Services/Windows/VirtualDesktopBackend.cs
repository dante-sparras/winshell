using System.Globalization;
using System.Security;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Windows;

/// <summary>Reads and changes virtual desktops. Tests use a stand-in; the shell uses <see cref="VirtualDesktopBackend"/>.</summary>
internal interface IVirtualDesktopBackend
{
    DesktopContract Contract { get; }

    /// <summary>
    /// Calls <paramref name="handler"/> each time Windows switches virtual desktop, until disposed, on the thread that
    /// first called <see cref="StartWatching"/>.
    /// </summary>
    /// <param name="handler">Runs for each switch.</param>
    /// <param name="failed">Gets each exception <paramref name="handler"/> throws.</param>
    IDisposable ListenSwitched(Action handler, Action<Exception> failed);

    /// <summary>Starts calling the switch handlers. False when Windows will not report a switch.</summary>
    bool StartWatching();

    /// <summary>Changes when a desktop is added or removed. Reading it makes no call into Explorer.</summary>
    string ListStamp { get; }

    bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error);

    /// <summary>Switches to the desktop. False, with why, when it did not.</summary>
    bool SwitchTo(Guid id, out string problem);

    /// <summary>Adds a desktop. Null, with why, when it was not added.</summary>
    Guid? Create(out string problem);

    /// <summary>Removes the desktop, moving its windows to <paramref name="fallback"/>. False, with why, when it did not.</summary>
    bool Remove(Guid id, Guid fallback, out string problem);
}

/// <summary>The build Windows reports, from <c>RtlGetVersion</c> rather than the version a manifest can change.</summary>
internal readonly record struct WindowsVersion(uint Build, uint Update)
{
    public override string ToString()
    {
        return Update == 0 ? Build.ToString(CultureInfo.InvariantCulture) : $"{Build}.{Update}";
    }

    public static WindowsVersion Current => _current ??= Read();

    private static WindowsVersion? _current;

    public static WindowsVersion Read()
    {
        return OsVersion.Build() is { } build ? new WindowsVersion(build, ReadUpdate()) : new WindowsVersion(0, 0);
    }

    private static uint ReadUpdate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("UBR") switch
            {
                int value when value > 0 => (uint)value,
                uint value => value,
                _ => 0
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
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
    // Listeners hands a handler's failure to the service that added it. Only a reporter that throws gets here, and
    // Callback would drop that failure anyway.
    private static readonly Action<Exception> Unreported = static _ => { };

    public static VirtualDesktopBackend Shared { get; } = new();

    private readonly Listeners<Action> _switched = new();
    private WinEventHook? _hook;

    private VirtualDesktopBackend()
    {
    }

    public IDisposable ListenSwitched(Action handler, Action<Exception> failed)
    {
        return _switched.Add(handler, failed);
    }

    public DesktopContract Contract
    {
        get
        {
            var version = WindowsVersion.Current;
            var contract = DesktopContract.ForBuild(version.Build);
            if (contract.Supported)
                return contract;
            return new DesktopContract(false,
                $"Windows {version} is outside the virtual desktop method table Winshell knows ({DesktopContract.OldestBuild} through {DesktopContract.NewestBuild}).");
        }
    }

    public bool StartWatching()
    {
        _hook ??= WinEventHook.Start(Win32.EVENT_SYSTEM_DESKTOPSWITCH, OnEvent, Unreported);
        return _hook is not null;
    }

    private void OnEvent(uint eventType, nint hwnd, int idObject, int idChild)
    {
        _switched.Raise(static handler => handler());
    }

    public string ListStamp =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops",
            "VirtualDesktopIDs", null) is byte[] ids
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

    public bool SwitchTo(Guid id, out string problem)
    {
        if (ImmersiveDesktops.SwitchTo(id, out problem))
            return true;
        problem = $"SwitchDesktop {problem}.";
        return false;
    }

    public Guid? Create(out string problem)
    {
        if (ImmersiveDesktops.Create(out problem) is { } id)
            return id;
        problem = $"CreateDesktop {problem}.";
        return null;
    }

    public bool Remove(Guid id, Guid fallback, out string problem)
    {
        problem = "";
        if (id == Guid.Empty || fallback == Guid.Empty || id == fallback)
            return false;
        if (ImmersiveDesktops.Remove(id, fallback, out problem))
            return true;
        problem = $"RemoveDesktop {problem}.";
        return false;
    }
}
