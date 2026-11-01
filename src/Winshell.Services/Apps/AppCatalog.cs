using System.Diagnostics;
using System.Runtime.InteropServices;
using global::Windows.ApplicationModel;
using global::Windows.Management.Deployment;

namespace Winshell.Apps;

/// <summary>The apps and the usage from one scan.</summary>
internal sealed class AppSnapshot
{
    public static readonly AppSnapshot Empty = new([], new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase));

    public AppSnapshot(IReadOnlyList<AppRecord> entries, IReadOnlyDictionary<string, AppUsage> usage)
    {
        Entries = entries;
        Usage = usage;
    }

    public IReadOnlyList<AppRecord> Entries { get; }

    public IReadOnlyDictionary<string, AppUsage> Usage { get; }

    public bool SameAs(AppSnapshot other)
    {
        if (Entries.Count != other.Entries.Count || Usage.Count != other.Usage.Count)
            return false;
        for (var i = 0; i < Entries.Count; i++)
        {
            if (!AppIndex.Same(Entries[i], other.Entries[i]))
                return false;
        }
        foreach (var (key, value) in Usage)
        {
            if (!other.Usage.TryGetValue(key, out var theirs) || theirs != value)
                return false;
        }
        return true;
    }
}

/// <summary>
/// The Start menu for one set of folders. The process shares <see cref="Shared"/>, which watches the real Programs
/// folders and package install. A test builds its own and points it at a temporary folder.
/// </summary>
internal sealed class AppCatalog : IDisposable
{
    public static IReadOnlyList<string> ProgramsFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
    ];

    public static AppCatalog Shared { get; } = new(
        ProgramsFolders(),
        PackagedApps.Read,
        AppUsageReader.Read,
        TimeSpan.FromSeconds(1),
        watchPackages: true,
        registerIcons: true);

    private readonly object _gate = new();
    private readonly IReadOnlyList<string> _folders;
    private readonly Func<IReadOnlyList<AppRecord>> _packaged;
    private readonly Func<IReadOnlyDictionary<string, AppUsage>> _usage;
    private readonly bool _watchPackages;
    private readonly bool _registerIcons;
    private readonly Log _log;
    private readonly Dictionary<string, DirectoryWatch> _watches = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _lost = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PackageCatalog? _packages;
    private Thread? _scanThread;
    private readonly long _made = TimeProvider.System.GetTimestamp();
    private long _quietUntil = long.MinValue;
    private bool _started, _disposed;
    private volatile bool _stop;
    private volatile AppSnapshot _snapshot = AppSnapshot.Empty;

    public AppCatalog(
        IReadOnlyList<string> folders,
        Func<IReadOnlyList<AppRecord>> packaged,
        Func<IReadOnlyDictionary<string, AppUsage>> usage,
        TimeSpan debounce,
        bool watchPackages,
        bool registerIcons,
        Log? log = null)
    {
        _folders = folders;
        _packaged = packaged;
        _usage = usage;
        _watchPackages = watchPackages;
        _registerIcons = registerIcons;
        _log = log ?? Log.Default;
        Debounce = debounce;
    }

    public TimeSpan Debounce { get; }

    /// <summary>Raised on a background thread after a scan changed the list or the usage.</summary>
    public event Action? Changed;

    public Task Ready => _ready.Task;

    public AppSnapshot Snapshot => _snapshot;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;
            _started = true;
        }
        if (_registerIcons)
            IconLookupReady?.Invoke(LookupIcon);
        // The scan thread is waiting before any watch is armed, so a change cannot arrive with nobody to scan it.
        _scanThread = new Thread(ScanLoop) { IsBackground = true, Name = "App scan" };
        _scanThread.Start();
        Poke(TimeSpan.Zero);
        foreach (var folder in _folders.Where(Directory.Exists))
            Arm(folder);
        if (_watchPackages)
        {
            try
            {
                _packages = PackageCatalog.OpenForCurrentUser();
                _packages.PackageInstalling += OnInstalling;
                _packages.PackageUninstalling += OnUninstalling;
                _packages.PackageUpdating += OnUpdating;
            }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException)
            {
                Debug.WriteLine(e);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _stop = true;
        _wake.Set();
        // The scan thread arms watches, so it stops first.
        _scanThread?.Join();
        foreach (var watch in _watches.Values)
            watch.Dispose();
        _watches.Clear();
        if (_packages is not null)
        {
            _packages.PackageInstalling -= OnInstalling;
            _packages.PackageUninstalling -= OnUninstalling;
            _packages.PackageUpdating -= OnUpdating;
            _packages = null;
        }
        _wake.Dispose();
        _ready.TrySetResult();
    }

    /// <summary>Hands a started catalog's icon lookup to whatever draws app icons.</summary>
    internal static Action<Func<string, string?>>? IconLookupReady { get; set; }

    private string? LookupIcon(string name)
    {
        if (name.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return name;
        return AppIndex.Find(_snapshot.Entries, name)?.Icon;
    }

    private void Arm(string folder)
    {
        // FileSystemWatcher delivers on a pool worker. A busy pool can hold a change, and the scan behind it, forever.
        var watch = DirectoryWatch.Start(
            folder,
            DirectoryWatch.FileChanges | DirectoryWatch.NameChanges,
            _ => Poke(Debounce),
            () => Lost(folder),
            subtree: true);
        if (watch is null)
            return;
        DirectoryWatch? old;
        lock (_gate)
        {
            _watches.Remove(folder, out old);
            _watches[folder] = watch;
        }
        old?.Dispose();
    }

    private void Lost(string folder)
    {
        lock (_gate)
            _lost.Add(folder);
        Poke(Debounce);
    }

    /// <summary>
    /// A watch ends when its folder goes away or its buffer overflows, and reports nothing after that. A scan arms it
    /// again first, so that scan sees whatever the old watch missed. A folder still missing waits for a later scan.
    /// </summary>
    private void RearmLost()
    {
        string[] folders;
        lock (_gate)
            folders = [.. _lost];
        foreach (var folder in folders.Where(Directory.Exists))
        {
            lock (_gate)
                _lost.Remove(folder);
            Arm(folder);
        }
    }

    private void OnInstalling(PackageCatalog sender, PackageInstallingEventArgs args)
    {
        if (args.IsComplete)
            Soon();
    }

    private void OnUninstalling(PackageCatalog sender, PackageUninstallingEventArgs args)
    {
        if (args.IsComplete)
            Soon();
    }

    private void OnUpdating(PackageCatalog sender, PackageUpdatingEventArgs args)
    {
        if (args.IsComplete)
            Soon();
    }

    /// <summary>Milliseconds since this catalog was made. The scan thread sleeps on a real wait handle, so this is real time too.</summary>
    private long Clock => (long)TimeProvider.System.GetElapsedTime(_made).TotalMilliseconds;

    // Installers write many files at once, so a scan waits until they have been quiet.
    private void Poke(TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var until = Clock + (long)delay.TotalMilliseconds;
            if (until > _quietUntil)
                _quietUntil = until;
        }
        _wake.Set();
    }

    private void Soon() => Poke(Debounce);

    private void ScanLoop()
    {
        while (!_stop)
        {
            _wake.WaitOne();
            if (_stop)
                return;
            while (!_stop)
            {
                int waitMs;
                lock (_gate)
                {
                    var remain = _quietUntil - Clock;
                    waitMs = remain <= 0 ? 0 : (int)Math.Min(remain, int.MaxValue);
                }
                if (waitMs == 0)
                    break;
                if (!_wake.WaitOne(waitMs))
                    break;
            }
            if (_stop)
                return;
            ScanOnce();
        }
    }

    private void ScanOnce()
    {
        try
        {
            RearmLost();
            var snapshot = Take();
            var changed = !_snapshot.SameAs(snapshot);
            _snapshot = snapshot;
            if (changed)
                Changed?.Invoke();
            _ready.TrySetResult();
        }
#pragma warning disable CA1031 // A failed scan is a finished empty result. The next file event tries again.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _log.Warn("apps", e.Message);
            _ready.TrySetResult();
        }
    }

    private AppSnapshot Take()
    {
        var entries = AppIndex.Combine(AppIndex.Shortcuts(_folders), ReadPackaged());
        return new AppSnapshot(entries, ReadUsage());
    }

    private IReadOnlyList<AppRecord> ReadPackaged()
    {
        try
        {
            return _packaged();
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return [];
        }
    }

    private IReadOnlyDictionary<string, AppUsage> ReadUsage()
    {
        try
        {
            return _usage();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

/// <summary>Packaged and unpackaged apps Windows shows in Start, from PackageManager.</summary>
internal static class PackagedApps
{
    public static IReadOnlyList<AppRecord> Read()
    {
        var result = new List<AppRecord>();
        IEnumerable<Package> packages;
        try
        {
            packages = new PackageManager().FindPackagesForUser("").ToList();
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            return result;
        }
        foreach (var package in packages)
        {
            try
            {
                if (package.IsFramework || package.IsResourcePackage)
                    continue;
                foreach (var app in package.GetAppListEntries())
                {
                    if (AppIndex.Packaged(app.AppUserModelId, app.DisplayInfo.DisplayName, app.DisplayInfo.Description) is { } entry)
                        result.Add(entry);
                }
            }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException or ArgumentException or FileNotFoundException or IOException or InvalidOperationException)
            {
                // A package being installed or removed can fail to answer; the next scan reads it again.
            }
        }
        return result;
    }
}

/// <summary>Opens an app the way Start does: a shortcut through the shell, a packaged app by its AppUserModelID.</summary>
internal static class AppLaunch
{
    public static bool Shortcut(string path, string startIn, Action<string> warn)
    {
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            if (startIn.Length > 0 && Directory.Exists(startIn))
                start.WorkingDirectory = startIn;
            using var _ = Process.Start(start);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            warn(e.Message);
            return false;
        }
    }

    public static void Packaged(string appUserModelId, string arguments, IEventLoop loop, Action<string> warn, Action started) =>
        Packaged(() => Activate(appUserModelId, arguments), () => OpenAppsFolder(appUserModelId), loop, warn, started);

    /// <summary>
    /// Runs <paramref name="activate"/> on its own STA thread and returns at once: activation returns only after
    /// the app is up, seconds on a cold start. Its outcome, and the <paramref name="fallback"/> when it failed,
    /// run on the loop. Each returns an error, or null when the app started.
    /// </summary>
    internal static void Packaged(Func<string?> activate, Func<string?> fallback, IEventLoop loop, Action<string> warn, Action started)
    {
        var thread = new Thread(() =>
        {
            var error = activate();
            loop.Post(() =>
            {
                if (error is not null)
                {
                    warn(error);
                    if (fallback() is { } failed)
                    {
                        warn(failed);
                        return;
                    }
                }
                started();
            });
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static string? Activate(string appUserModelId, string arguments)
    {
        object? created = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"));
            created = type is null ? null : Activator.CreateInstance(type);
            if (created is not IApplicationActivationManager manager)
                return "The activation manager is not available.";
            manager.ActivateApplication(appUserModelId, arguments ?? "", 0, out _);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            if (created is not null && Marshal.IsComObject(created))
                Marshal.ReleaseComObject(created);
        }
    }

    private static string? OpenAppsFolder(string appUserModelId)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", AppIndex.AppsFolderPrefix + appUserModelId) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return e.Message;
        }
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
    }
}
