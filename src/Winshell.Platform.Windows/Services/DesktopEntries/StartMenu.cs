using System.Diagnostics;
using global::Windows.ApplicationModel;
using global::Windows.Management.Deployment;

namespace Winshell.Platform.Windows.Services.DesktopEntries;

/// <summary>One app of the Start menu, as a desktop entry describes one.</summary>
internal sealed record AppEntry(
    string Id,
    string Name,
    string Comment,
    string Icon,
    string ExecString,
    IReadOnlyList<string> Command,
    string WorkingDirectory,
    string StartupClass,
    IReadOnlyList<string> Categories,
    bool NoDisplay,
    string LaunchPath);

/// <summary>
/// The Start menu's apps for the whole process: the shortcuts in the common and the user's Programs folders, and the
/// packaged apps. It is read on a background thread at first use and again when a folder or a package changes.
/// </summary>
internal static class StartMenu
{
    private static readonly object Gate = new();
    private static readonly List<FileSystemWatcher> Watchers = [];
    private static Timer? _debounce;
    private static PackageCatalog? _catalog;
    private static bool _started, _scanning, _queued;
    private static volatile IReadOnlyList<AppEntry> _entries = [];

    /// <summary>Raised on a background thread after a scan changed the list.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<AppEntry> Entries => _entries;

    public static IEnumerable<string> Folders =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
    ];

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_started)
                return;
            _started = true;
        }
        IconProvider.DesktopEntriesLookup = IconFor;
        _debounce = new Timer(_ => Rescan());
        foreach (var folder in Folders.Where(Directory.Exists))
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
            Watchers.Add(watcher);
        }
        try
        {
            _catalog = PackageCatalog.OpenForCurrentUser();
            _catalog.PackageInstalling += (_, e) => { if (e.IsComplete) Soon(); };
            _catalog.PackageUninstalling += (_, e) => { if (e.IsComplete) Soon(); };
            _catalog.PackageUpdating += (_, e) => { if (e.IsComplete) Soon(); };
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
        }
        Rescan();
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e) => Soon();

    // Installers write many files at once, so a scan waits until they have been quiet for a second.
    private static void Soon() => _debounce?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

    private static void Rescan()
    {
        lock (Gate)
        {
            if (_scanning)
            {
                _queued = true;
                return;
            }
            _scanning = true;
        }
        Task.Run(() =>
        {
            while (true)
            {
                var entries = Scan();
                var changed = !entries.SequenceEqual(_entries, EntryComparer.Instance);
                _entries = entries;
                if (changed)
                    Changed?.Invoke();
                lock (Gate)
                {
                    if (!_queued)
                    {
                        _scanning = false;
                        return;
                    }
                    _queued = false;
                }
            }
        });
    }

    private static List<AppEntry> Scan()
    {
        var byId = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        // The user's folder comes second, so its shortcut wins over a common one of the same name.
        foreach (var folder in Folders.Where(Directory.Exists))
        {
            foreach (var path in SafeFiles(folder))
            {
                if (FromShortcut(folder, path) is { } entry)
                    byId[entry.Id] = entry;
            }
        }
        foreach (var entry in PackagedApps())
            byId.TryAdd(entry.Id, entry);
        return byId.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static AppEntry? FromShortcut(string folder, string path)
    {
        if (ShellLink.Read(path) is not { } link)
            return null;
        var name = Path.GetFileNameWithoutExtension(path);
        var relative = Path.GetRelativePath(folder, Path.GetDirectoryName(path) ?? folder);
        var categories = relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var hasTarget = link.Target.Length > 0 && File.Exists(link.Target);
        List<string> command = hasTarget ? [link.Target, .. ShellLink.SplitArguments(link.Arguments)] : ["explorer.exe", path];
        var exec = hasTarget ? $"\"{link.Target}\"" + (link.Arguments.Length > 0 ? " " + link.Arguments : "") : $"explorer.exe \"{path}\"";
        var startupClass = link.AppUserModelId.Length > 0 ? link.AppUserModelId
            : hasTarget && Path.GetExtension(link.Target).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(link.Target).ToLowerInvariant()
            : "";
        // Uninstallers and readmes sit next to many apps in the Start menu; launchers have no use for them.
        var noDisplay = name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || (hasTarget && Path.GetExtension(link.Target).ToLowerInvariant() is ".txt" or ".chm" or ".pdf" or ".htm" or ".html" or ".url");
        return new AppEntry(
            name,
            name,
            link.Description,
            // The shell draws a shortcut's own icon, whatever its icon location says.
            path,
            exec,
            command,
            link.WorkingDirectory.Length > 0 ? link.WorkingDirectory : hasTarget ? Path.GetDirectoryName(link.Target) ?? "" : "",
            startupClass,
            categories,
            noDisplay,
            path);
    }

    private static List<AppEntry> PackagedApps()
    {
        var result = new List<AppEntry>();
        IEnumerable<Package> packages;
        try
        {
            packages = new PackageManager().FindPackagesForUser("").ToList();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
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
                    var aumid = app.AppUserModelId;
                    var name = app.DisplayInfo.DisplayName;
                    if (string.IsNullOrEmpty(aumid) || string.IsNullOrEmpty(name))
                        continue;
                    var launch = @"shell:AppsFolder\" + aumid;
                    result.Add(new AppEntry(aumid, name, app.DisplayInfo.Description ?? "", launch, $"explorer.exe {launch}", ["explorer.exe", launch], "", aumid, [], false, launch));
                }
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException or FileNotFoundException)
            {
                // A package being installed or removed can fail to answer; it is read again on the next scan.
            }
        }
        return result;
    }

    /// <summary>Finds an entry the way DesktopEntries.heuristicLookup does: by id, then by startup class, then by name.</summary>
    public static AppEntry? Lookup(IReadOnlyList<AppEntry> entries, string name)
    {
        if (name.Length == 0)
            return null;
        return entries.FirstOrDefault(e => e.Id == name)
            ?? entries.FirstOrDefault(e => e.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e => e.StartupClass == name)
            ?? entries.FirstOrDefault(e => e.StartupClass.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>For the icon provider: the file the shell draws an app's icon from.</summary>
    private static string? IconFor(string name) =>
        name.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? name : Lookup(_entries, name)?.Icon;

    /// <summary>Opens an entry as the Start menu does: the shortcut itself, or the packaged app by its id.</summary>
    public static void Launch(AppEntry entry, Action<string> warn)
    {
        try
        {
            var start = entry.LaunchPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", entry.LaunchPath)
                : new ProcessStartInfo(entry.LaunchPath) { UseShellExecute = true, WorkingDirectory = entry.WorkingDirectory };
            using var _ = Process.Start(start);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            warn($"Failed to launch {entry.Name}: {e.Message}");
        }
    }

    private sealed class EntryComparer : IEqualityComparer<AppEntry>
    {
        public static readonly EntryComparer Instance = new();

        public bool Equals(AppEntry? a, AppEntry? b) =>
            a is not null && b is not null && a with { Command = b.Command, Categories = b.Categories } == b
            && a.Command.SequenceEqual(b.Command) && a.Categories.SequenceEqual(b.Categories);

        public int GetHashCode(AppEntry e) => e.Id.GetHashCode(StringComparison.Ordinal);
    }
}
