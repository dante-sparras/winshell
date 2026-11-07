using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Power;

/// <summary>How Winshell reaches a monitor to read and set its brightness.</summary>
public enum BrightnessConnection
{
    /// <summary>A built-in panel, such as a laptop screen, set through WMI.</summary>
    Internal = 0,

    /// <summary>An external monitor, set over DDC/CI.</summary>
    External = 1
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

internal interface IBrightnessBackend : IDisposable
{
    /// <summary>Windows reported a change to a built-in panel's brightness. Raised on any thread.</summary>
    event Action? Changed;

    /// <summary>Reads the monitors. The first read starts watching Windows until the backend is disposed.</summary>
    Task<BrightnessSnapshot> ReadAsync(CancellationToken cancellation);

    /// <summary>
    /// Sets one monitor to a brightness already in its range. Returns why Windows did not, or null. One write can take
    /// hundreds of milliseconds.
    /// </summary>
    string? Set(string deviceName, int brightness);
}

/// <summary>
/// Monitor brightness. An internal panel comes from WMI. An external monitor comes from one DDC/CI read.
/// Reads happen when the shell starts, when the shell's screens change, when an internal panel reports a change, and
/// after a burst of writes ends, so external monitors are not polled. A monitor whose read fails stays at its
/// last reading while it is connected.
/// </summary>
/// <param name="log">Where a WMI failure is reported.</param>
internal sealed class BrightnessBackend(Log log) : IBrightnessBackend
{
    private volatile IReadOnlyList<BrightnessInfo> _monitors = [];
    private bool _watching;
    private ManagementEventWatcher? _watcher;

    public event Action? Changed;

    public Task<BrightnessSnapshot> ReadAsync(CancellationToken cancellation)
    {
        return Task.Run(Read, cancellation);
    }

    private BrightnessSnapshot Read()
    {
        if (!_watching)
        {
            _watching = true;
            // Starting a WMI watcher takes a few hundred milliseconds, so it starts here, off the loop.
            WatchInternal();
        }

        _monitors = Combine(Screens.Enumerate(), ReadWmi(), ReadExternal, _monitors);
        return new BrightnessSnapshot(true, _monitors);
    }

    /// <summary>Stops watching Windows.</summary>
    public void Dispose()
    {
        if (!_watching)
            return;
        _watching = false;
        if (_watcher is { } watcher)
        {
            watcher.EventArrived -= OnPanelChanged;
            watcher.Stop();
            watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnPanelChanged(object sender, EventArrivedEventArgs e)
    {
        Changed?.Invoke();
    }

    private void WatchInternal()
    {
        try
        {
            var watcher = new ManagementEventWatcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessEvent");
            watcher.EventArrived += OnPanelChanged;
            watcher.Start();
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
        {
            WmiFailed("watch built-in panel brightness", ex);
        }
    }

    /// <summary>
    /// The monitors with a brightness. A screen whose model code matches a WMI panel is that panel, named by the
    /// screen's model. Any other screen is read with <paramref name="readExternal"/>. A panel no screen matches is
    /// named by its WMI instance.
    /// </summary>
    internal static List<BrightnessInfo> Combine(IReadOnlyList<ConnectedMonitor> screens,
        IReadOnlyList<WmiPanel> panels, Func<nint, (int Minimum, int Current, int Maximum)?> readExternal,
        IReadOnlyList<BrightnessInfo> previous)
    {
        var unmatched = panels.ToList();
        var result = new List<BrightnessInfo>();
        foreach (var screen in screens)
        {
            var panel = unmatched.FirstOrDefault(p =>
                screen.ModelCode.Length > 0
                && string.Equals(PanelModelCode(p.Instance), screen.ModelCode, StringComparison.OrdinalIgnoreCase));
            if (panel is not null)
            {
                unmatched.Remove(panel);
                result.Add(Internal(panel, screen.Info.Model.Length > 0 ? screen.Info.Model : panel.Instance));
                continue;
            }

            var external = External(screen.Info.Id, screen.Info.Model.Length > 0 ? screen.Info.Model : screen.Info.Name,
                readExternal(screen.Handle), previous);
            if (external is not null)
                result.Add(external);
        }

        result.AddRange(unmatched.Select(p => Internal(p, p.Instance)));
        return result;
    }

    private static BrightnessInfo Internal(WmiPanel panel, string name)
    {
        return new BrightnessInfo(panel.Instance, name, BrightnessConnection.Internal, panel.Minimum, panel.Maximum,
            panel.Current);
    }

    /// <summary>An external monitor's info. A failed read (<paramref name="reading"/> is null) keeps the monitor's last reading, so one bad read does not remove it.</summary>
    internal static BrightnessInfo? External(string id, string name, (int Minimum, int Current, int Maximum)? reading,
        IReadOnlyList<BrightnessInfo> previous)
    {
        return reading is { } r
            ? new BrightnessInfo(id, name, BrightnessConnection.External, r.Minimum, r.Maximum, r.Current)
            : previous.FirstOrDefault(p => p.Connection == BrightnessConnection.External && p.DeviceName == id);
    }

    /// <summary>The PnP model code, such as <c>AUS32A8</c>, in a WMI instance name like <c>DISPLAY\AUS32A8\...</c>.</summary>
    private static string PanelModelCode(string instance)
    {
        var parts = instance.Split('\\');
        return parts.Length > 1 ? parts[1] : "";
    }

    private static (int Minimum, int Current, int Maximum)? ReadExternal(nint monitor)
    {
        return MonitorBrightness.TryRead(monitor, out var min, out var current, out var max)
            ? (min, current, max)
            : null;
    }

    public string? Set(string deviceName, int brightness)
    {
        var monitor = _monitors.FirstOrDefault(m => m.DeviceName == deviceName);
        if (monitor is null)
            return "the monitor is gone";
        try
        {
            return monitor.Connection == BrightnessConnection.Internal
                ? SetWmi(deviceName, brightness)
                : SetDdc(deviceName, brightness);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    internal sealed record WmiPanel(string Instance, int Minimum, int Maximum, int Current);

    private List<WmiPanel> ReadWmi()
    {
        var panels = new List<WmiPanel>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI",
                "SELECT InstanceName, CurrentBrightness, Level FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (var monitor in results)
                using (monitor)
                {
                    var instance = monitor["InstanceName"] as string ?? "";
                    var current = Convert.ToInt32(monitor["CurrentBrightness"], CultureInfo.InvariantCulture);
                    var (min, max) = Range(monitor["Level"]);
                    panels.Add(new WmiPanel(instance, min, max, current));
                }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException
                                       or InvalidCastException)
        {
            WmiFailed("read built-in panel brightness", ex);
        }

        return panels;
    }

    /// <summary>
    /// Logs a WMI failure, except the "not supported" Windows answers when no monitor takes brightness through WMI, as
    /// on most desktops.
    /// </summary>
    private void WmiFailed(string doing, Exception ex)
    {
        if (ex is ManagementException { ErrorCode: ManagementStatus.NotSupported })
            return;
        log.Warn("power", $"Could not {doing}: {ex.Message}");
    }

    private static string? SetWmi(string instance, int brightness)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        using var results = searcher.Get();
        foreach (ManagementObject monitor in results)
            using (monitor)
            {
                if (!string.Equals(monitor["InstanceName"] as string, instance, StringComparison.OrdinalIgnoreCase))
                    continue;
                // A timeout in seconds, then the level; WmiMonitorBrightness levels run from 0 to 100.
                monitor.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)brightness });
                return null;
            }

        return "Windows no longer lists the panel";
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

    private static string? SetDdc(string deviceName, int brightness)
    {
        if (Screens.Find(deviceName) is not { } screen)
            return "the monitor is gone";
        return MonitorBrightness.TrySet(screen.Handle, (uint)brightness)
            ? null
            : "the monitor did not answer over DDC/CI";
    }
}

/// <summary>
/// Writes each monitor's brightness on a background thread, one write at a time. A value asked for while a
/// write runs replaces the one waiting, so a slider drag writes the newest value and drops the steps between.
/// <paramref name="drained"/> runs each time a monitor's writes end. A write that throws is reported to
/// <paramref name="log"/>.
/// </summary>
internal sealed class BrightnessWriter(Func<string, int, string?> write, Log log, Action? drained = null)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (int Brightness, Action<string> Failed)> _pending = [];
    private readonly HashSet<string> _running = [];

    /// <summary>Whether a write for the monitor is running or waiting.</summary>
    public bool IsWriting(string deviceName)
    {
        lock (_lock)
        {
            return _running.Contains(deviceName);
        }
    }

    /// <summary>Writes <paramref name="brightness"/> soon, and calls <paramref name="failed"/> with the reason if Windows refuses it.</summary>
    public void Request(string deviceName, int brightness, Action<string> failed)
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
                (int Brightness, Action<string> Failed) next;
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
                if (write(deviceName, next.Brightness) is { } reason)
                    next.Failed(reason);
            }

            drained?.Invoke();
        }
#pragma warning disable CA1031 // A write that fails is reported. On this thread it would end the shell.
        catch (Exception e)
#pragma warning restore CA1031
        {
            lock (_lock)
            {
                _running.Remove(deviceName);
            }

            log.Warn("power", e.Message);
        }
    }
}
