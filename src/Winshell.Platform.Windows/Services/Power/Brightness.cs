using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Services.Power;

public enum BrightnessConnection
{
    Internal = 0,
    External = 1,
}

internal sealed record BrightnessInfo(
    string DeviceName,
    string Name,
    BrightnessConnection Connection,
    int Minimum,
    int Maximum,
    int Current);

internal sealed record BrightnessSnapshot(bool Ready, IReadOnlyList<BrightnessInfo> Monitors)
{
    public static readonly BrightnessSnapshot Initial = new(false, []);
}

/// <summary>
/// Monitor brightness. An internal panel comes from WMI. An external monitor comes from one DDC/CI read.
/// Reads happen when the shell starts and when an internal panel reports a change, so external monitors
/// are not polled.
/// </summary>
internal sealed partial class BrightnessBackend : SnapshotSource<BrightnessSnapshot>
{
    public static readonly BrightnessBackend Instance = new();

    private ManagementEventWatcher? _watcher;

    private BrightnessBackend()
        : base(BrightnessSnapshot.Initial)
    {
    }

    protected override string Category => "power";

    protected override async Task StartAsync()
    {
        WatchInternal();
        await RefreshNowAsync().ConfigureAwait(false);
    }

    private void WatchInternal()
    {
        try
        {
            var watcher = new ManagementEventWatcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessEvent");
            watcher.EventArrived += (_, _) => Refresh();
            watcher.Start();
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }
    }

    protected override Task<BrightnessSnapshot> ReadAsync()
        => Task.FromResult(new BrightnessSnapshot(true, ReadMonitors()));

    private static List<BrightnessInfo> ReadMonitors()
    {
        var panels = ReadWmi();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BrightnessInfo>();
        foreach (var screen in Screens.Enumerate())
        {
            var token = ModelToken(PnpId(screen.Info.Id));
            var panel = panels.FirstOrDefault(p =>
                token.Length > 0
                && !used.Contains(p.Instance)
                && string.Equals(ModelToken(p.Instance), token, StringComparison.OrdinalIgnoreCase));
            if (panel is not null)
            {
                used.Add(panel.Instance);
                var name = screen.Info.Model.Length > 0 ? screen.Info.Model : panel.Instance;
                result.Add(new BrightnessInfo(panel.Instance, name, BrightnessConnection.Internal, panel.Minimum, panel.Maximum, panel.Current));
                continue;
            }
            if (TryReadDdc(screen.Handle, out var min, out var current, out var max))
            {
                var name = screen.Info.Model.Length > 0 ? screen.Info.Model : screen.Info.Name;
                result.Add(new BrightnessInfo(screen.Info.Id, name, BrightnessConnection.External, min, max, current));
            }
        }
        foreach (var panel in panels.Where(p => !used.Contains(p.Instance)))
            result.Add(new BrightnessInfo(panel.Instance, panel.Instance, BrightnessConnection.Internal, panel.Minimum, panel.Maximum, panel.Current));
        return result;
    }

    /// <summary>Sets one monitor. Returns a Windows error, or 0. Callers must not use this from tests.</summary>
    public uint Set(string deviceName, int brightness)
    {
        var monitor = Current.Monitors.FirstOrDefault(m => m.DeviceName == deviceName);
        if (monitor is null)
            return 1168;
        var value = Math.Clamp(brightness, monitor.Minimum, monitor.Maximum);
        bool ok;
        try
        {
            ok = monitor.Connection == BrightnessConnection.Internal
                ? TrySetWmi(deviceName, value)
                : TrySetDdc(deviceName, (uint)value);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            ok = false;
        }
        Refresh();
        return ok ? 0u : 1u;
    }

    private sealed record WmiPanel(string Instance, int Minimum, int Maximum, int Current);

    private static List<WmiPanel> ReadWmi()
    {
        var panels = new List<WmiPanel>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentBrightness, Level FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (ManagementBaseObject monitor in results)
            {
                using (monitor)
                {
                    var instance = monitor["InstanceName"] as string ?? "";
                    var current = Convert.ToInt32(monitor["CurrentBrightness"], CultureInfo.InvariantCulture);
                    var (min, max) = Range(monitor["Level"]);
                    panels.Add(new WmiPanel(instance, min, max, current));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidCastException)
        {
        }
        return panels;
    }

    private static bool TrySetWmi(string instance, int brightness)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        using var results = searcher.Get();
        foreach (ManagementObject monitor in results)
        {
            using (monitor)
            {
                if (!string.Equals(monitor["InstanceName"] as string, instance, StringComparison.OrdinalIgnoreCase))
                    continue;
                monitor.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)Math.Clamp(brightness, 0, 100) });
                return true;
            }
        }
        return false;
    }

    private static (int Minimum, int Maximum) Range(object? level)
    {
        if (level is not Array array || array.Length == 0)
            return (0, 100);
        var values = new List<int>(array.Length);
        foreach (var item in array)
            values.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
        return (values.Min(), values.Max());
    }

    private static unsafe string PnpId(string deviceName)
    {
        var info = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (!Win32.EnumDisplayDevicesW(deviceName, 0, ref info, 1))
            return "";
        return new string(info.DeviceID).TrimEnd('\0');
    }

    /// <summary>The model code in <c>MONITOR\GSM5B7F\...</c> or <c>DISPLAY\GSM5B7F\...</c>.</summary>
    private static string ModelToken(string id)
    {
        var parts = id.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : "";
    }

    private static unsafe bool TryReadDdc(nint handle, out int min, out int current, out int max)
    {
        min = current = max = 0;
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count) || count == 0 || count > 16)
            return false;
        var monitors = stackalloc PHYSICAL_MONITOR[(int)count];
        if (!GetPhysicalMonitorsFromHMONITOR(handle, count, monitors))
            return false;
        try
        {
            if (!GetMonitorBrightness(monitors[0].hPhysicalMonitor, out var lo, out var now, out var hi))
                return false;
            min = (int)lo;
            current = (int)now;
            max = (int)hi;
            return max >= min;
        }
        finally
        {
            DestroyPhysicalMonitors(count, monitors);
        }
    }

    private static unsafe bool TrySetDdc(string deviceName, uint brightness)
    {
        var screen = Screens.Find(deviceName);
        if (screen is null)
            return false;
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(screen.Handle, out var count) || count == 0 || count > 16)
            return false;
        var monitors = stackalloc PHYSICAL_MONITOR[(int)count];
        if (!GetPhysicalMonitorsFromHMONITOR(screen.Handle, count, monitors))
            return false;
        try
        {
            return SetMonitorBrightness(monitors[0].hPhysicalMonitor, brightness);
        }
        finally
        {
            DestroyPhysicalMonitors(count, monitors);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;
        public fixed char szPhysicalMonitorDescription[128];
    }

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint hMonitor, out uint count);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetPhysicalMonitorsFromHMONITOR(nint hMonitor, uint count, PHYSICAL_MONITOR* monitors);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorBrightness(nint hMonitor, out uint minimum, out uint current, out uint maximum);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMonitorBrightness(nint hMonitor, uint brightness);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR* monitors);
}

/// <summary>One monitor's brightness. The setter writes the panel; tests only read.</summary>
public sealed class BrightnessMonitor : QObject
{
    private BrightnessInfo _info = new("", "", BrightnessConnection.External, 0, 100, 0);

    [Qml] public string DeviceName { get { Track(); return _info.DeviceName; } }

    [Qml] public string Name { get { Track(); return _info.Name; } }

    [Qml] public BrightnessConnection Connection { get { Track(); return _info.Connection; } }

    [Qml] public int Minimum { get { Track(); return _info.Minimum; } }

    [Qml] public int Maximum { get { Track(); return _info.Maximum; } }

    [Qml]
    public int Brightness
    {
        get { Track(); return _info.Current; }
        set
        {
            if (value == _info.Current)
                return;
            var error = BrightnessBackend.Instance.Set(_info.DeviceName, value);
            if (error != 0)
                ServiceSupport.Warn(this, "power", $"Windows did not set the brightness of {_info.Name} (error {error}).");
        }
    }

    internal void Update(BrightnessInfo info)
    {
        var old = _info;
        _info = info;
        Changed(old.DeviceName != info.DeviceName, "deviceName");
        Changed(old.Name != info.Name, "name");
        Changed(old.Connection != info.Connection, "connection");
        Changed(old.Minimum != info.Minimum, "minimum");
        Changed(old.Maximum != info.Maximum, "maximum");
        Changed(old.Current != info.Current, "brightness");
    }

    private void Changed(bool changed, string property)
    {
        if (changed)
            NotifyChanged(property);
    }
}

/// <summary>The <c>Brightness</c> singleton.</summary>
public sealed class BrightnessService : QObject
{
    private readonly IDisposable _subscription;
    private readonly ObjectModel _monitors = new();
    private readonly List<BrightnessMonitor> _monitorObjects = [];
    private bool _ready;

    internal BrightnessService(QmlEngine engine)
    {
        _monitors.SetOwner(this);
        Apply(BrightnessBackend.Instance.Current);
        _subscription = BrightnessBackend.Instance.Subscribe(s => engine.Loop.Post(() => Apply(s)));
    }

    [Qml] public ObjectModel Monitors => _monitors;

    [Qml] public bool Ready { get { Track(); return _ready; } }

    private void Apply(BrightnessSnapshot snapshot)
    {
        if (IsDestroyed)
            return;
        var next = new List<BrightnessMonitor>();
        foreach (var info in snapshot.Monitors)
        {
            var monitor = _monitorObjects.FirstOrDefault(m => m.DeviceName == info.DeviceName && !next.Contains(m));
            if (monitor is null)
            {
                monitor = new BrightnessMonitor();
                monitor.SetOwner(this);
            }
            monitor.Update(info);
            next.Add(monitor);
        }
        foreach (var gone in _monitorObjects.Except(next).ToList())
            gone.Destroy();
        _monitorObjects.Clear();
        _monitorObjects.AddRange(next);
        _monitors.Set([.. next]);
        SetField(ref _ready, snapshot.Ready);
    }

    protected override void OnDestroy() => _subscription.Dispose();
}
