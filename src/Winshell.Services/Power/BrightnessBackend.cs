using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Winshell.Native;

namespace Winshell.Power;

/// <summary>How Winshell reaches a monitor to read and set its brightness.</summary>
public enum BrightnessConnection
{
    /// <summary>A built-in panel, such as a laptop screen, set through WMI.</summary>
    Internal = 0,
    /// <summary>An external monitor, set over DDC/CI.</summary>
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
/// Reads happen when the shell starts, when the displays change, when an internal panel reports a change, and
/// after a burst of writes ends, so external monitors are not polled. A monitor whose read fails stays at its
/// last reading while it is connected.
/// </summary>
internal sealed class BrightnessBackend : SnapshotSource<BrightnessSnapshot>
{
    public static readonly BrightnessBackend Instance = new();

    private ManagementEventWatcher? _watcher;

    private BrightnessBackend()
        : base(BrightnessSnapshot.Initial)
        => Writer = new BrightnessWriter(Set, Refresh);

    /// <summary>Writes brightness off the UI thread, since one write can take hundreds of milliseconds.</summary>
    public BrightnessWriter Writer { get; }

    protected override string Category => "power";

    protected override async Task StartAsync()
    {
        SystemEvents.DisplaySettingsChanged += (_, _) => Refresh();
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
        => Task.FromResult(new BrightnessSnapshot(true, ReadMonitors(Current.Monitors)));

    private static List<BrightnessInfo> ReadMonitors(IReadOnlyList<BrightnessInfo> previous)
    {
        var panels = ReadWmi();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BrightnessInfo>();
        foreach (var screen in Screens.Enumerate())
        {
            var token = ModelToken(DisplayDevice.MonitorId(screen.Info.Id));
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
            var reading = MonitorBrightness.TryRead(screen.Handle, out var min, out var current, out var max) ? (min, current, max) : ((int, int, int)?)null;
            var external = External(screen.Info.Id, screen.Info.Model.Length > 0 ? screen.Info.Model : screen.Info.Name, reading, previous);
            if (external is not null)
                result.Add(external);
        }
        foreach (var panel in panels.Where(p => !used.Contains(p.Instance)))
            result.Add(new BrightnessInfo(panel.Instance, panel.Instance, BrightnessConnection.Internal, panel.Minimum, panel.Maximum, panel.Current));
        return result;
    }

    /// <summary>An external monitor's info. A failed read (<paramref name="reading"/> is null) keeps the monitor's last reading, so one bad read does not remove it.</summary>
    internal static BrightnessInfo? External(string id, string name, (int Minimum, int Current, int Maximum)? reading, IReadOnlyList<BrightnessInfo> previous)
        => reading is { } r
            ? new BrightnessInfo(id, name, BrightnessConnection.External, r.Minimum, r.Maximum, r.Current)
            : previous.FirstOrDefault(p => p.Connection == BrightnessConnection.External && p.DeviceName == id);

    /// <summary>Sets one monitor. Returns a Windows error, or 0. Callers must not use this from tests.</summary>
    private uint Set(string deviceName, int brightness)
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

    /// <summary>The model code in <c>MONITOR\GSM5B7F\...</c> or <c>DISPLAY\GSM5B7F\...</c>.</summary>
    private static string ModelToken(string id)
    {
        var parts = id.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : "";
    }

    private static bool TrySetDdc(string deviceName, uint brightness)
        => Screens.Find(deviceName) is { } screen && MonitorBrightness.TrySet(screen.Handle, brightness);
}

/// <summary>
/// Writes each monitor's brightness on a background thread, one write at a time. A value asked for while a
/// write runs replaces the one waiting, so a slider drag writes the newest value and drops the steps between.
/// <paramref name="drained"/> runs each time a monitor's writes end.
/// </summary>
internal sealed class BrightnessWriter(Func<string, int, uint> write, Action? drained = null)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (int Brightness, Action<uint> Failed)> _pending = [];
    private readonly HashSet<string> _running = [];

    /// <summary>Whether a write for the monitor is running or waiting.</summary>
    public bool IsWriting(string deviceName)
    {
        lock (_lock)
            return _running.Contains(deviceName);
    }

    /// <summary>Writes <paramref name="brightness"/> soon, and calls <paramref name="failed"/> with the error if Windows refuses it.</summary>
    public void Request(string deviceName, int brightness, Action<uint> failed)
    {
        lock (_lock)
        {
            _pending[deviceName] = (brightness, failed);
            if (!_running.Add(deviceName))
                return;
        }
        // A thread of its own: a write blocks for hundreds of milliseconds, and it must not wait behind a busy pool.
        new Thread(() => Drain(deviceName)) { IsBackground = true, Name = "Brightness write" }.Start();
    }

    private void Drain(string deviceName)
    {
        int? last = null;
        try
        {
            while (true)
            {
                (int Brightness, Action<uint> Failed) next;
                lock (_lock)
                {
                    if (!_pending.Remove(deviceName, out next))
                    {
                        _running.Remove(deviceName);
                        break;
                    }
                }
                if (next.Brightness == last)
                    continue;
                last = next.Brightness;
                var error = write(deviceName, next.Brightness);
                if (error != 0)
                    next.Failed(error);
            }
            drained?.Invoke();
        }
#pragma warning disable CA1031 // A write that fails is reported. On this thread it would end the shell.
        catch (Exception e)
#pragma warning restore CA1031
        {
            lock (_lock)
                _running.Remove(deviceName);
            Log.Default.Warn("power", e.Message);
        }
    }
}
