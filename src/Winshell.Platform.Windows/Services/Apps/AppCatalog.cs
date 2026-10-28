using System.Diagnostics;
using System.Runtime.InteropServices;
using global::Windows.ApplicationModel;
using global::Windows.Management.Deployment;

namespace Winshell.Platform.Windows.Services.Apps;

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
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _debounce;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PackageCatalog? _packages;
    private bool _started, _scanning, _queued, _disposed;
    private volatile AppSnapshot _snapshot = AppSnapshot.Empty;

    public AppCatalog(
        IReadOnlyList<string> folders,
        Func<IReadOnlyList<AppRecord>> packaged,
        Func<IReadOnlyDictionary<string, AppUsage>> usage,
        TimeSpan debounce,
        bool watchPackages,
        bool registerIcons)
    {
        _folders = folders;
        _packaged = packaged;
        _usage = usage;
        _watchPackages = watchPackages;
        _registerIcons = registerIcons;
        _debounce = new Timer(_ => Rescan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
            IconProvider.AppIconLookup = LookupIcon;
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Changed += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
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
        Rescan();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        if (_packages is not null)
        {
            _packages.PackageInstalling -= OnInstalling;
            _packages.PackageUninstalling -= OnUninstalling;
            _packages.PackageUpdating -= OnUpdating;
            _packages = null;
        }
        _debounce.Dispose();
        _ready.TrySetResult();
    }

    private string? LookupIcon(string name)
    {
        if (name.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return name;
        return AppIndex.Find(_snapshot.Entries, name)?.Icon;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => Soon();

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

    // Installers write many files at once, so a scan waits until they have been quiet.
    private void Soon() => _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);

    private void Rescan()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_scanning)
            {
                _queued = true;
                return;
            }
            _scanning = true;
        }
        Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    var snapshot = Take();
                    var changed = !_snapshot.SameAs(snapshot);
                    _snapshot = snapshot;
                    if (changed)
                        Changed?.Invoke();
                    _ready.TrySetResult();
                    lock (_gate)
                    {
                        if (_disposed || !_queued)
                        {
                            _scanning = false;
                            return;
                        }
                        _queued = false;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                lock (_gate)
                    _scanning = false;
            }
        });
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

    public static bool Packaged(string appUserModelId, string arguments, Action<string> warn)
    {
        var activated = Activate(appUserModelId, arguments, out var error);
        if (activated == ActivateResult.Started)
            return true;
        if (activated == ActivateResult.TimedOut)
        {
            warn("The packaged app did not start in time.");
            return false;
        }
        if (error is not null)
            warn(error);
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", AppIndex.AppsFolderPrefix + appUserModelId) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            warn(e.Message);
            return false;
        }
    }

    private static ActivateResult Activate(string appUserModelId, string arguments, out string? error)
    {
        error = null;
        var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"));
        if (type is null)
            return ActivateResult.Failed;
        Exception? thrown = null;
        var started = false;
        var thread = new Thread(() =>
        {
            object? created = null;
            try
            {
                created = Activator.CreateInstance(type);
                if (created is not IApplicationActivationManager manager)
                    return;
                manager.ActivateApplication(appUserModelId, arguments ?? "", 0, out _);
                started = true;
            }
            catch (Exception e)
            {
                thrown = e;
            }
            finally
            {
                if (created is not null && Marshal.IsComObject(created))
                    Marshal.ReleaseComObject(created);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
            return ActivateResult.TimedOut;
        if (thrown is not null)
            error = thrown.Message;
        return started ? ActivateResult.Started : ActivateResult.Failed;
    }

    private enum ActivateResult { Started, Failed, TimedOut }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
    }
}
