using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Updates;

/// <summary>Where an update is: the <c>UpdateStatus</c> enum of <c>Updates.status</c>.</summary>
public enum UpdateStatus
{
    /// <summary>Not checked yet in this run.</summary>
    Idle,
    Checking,
    UpToDate,
    Downloading,
    /// <summary>Downloaded, its hash checked and unzipped. It applies when Winshell next starts.</summary>
    Ready,
    Failed,
}

/// <summary>An update put in place as Winshell started, or, with <see cref="Error"/>, one that was not.</summary>
public sealed record InstalledUpdate(string Version, string? Error);

/// <summary>What the updater knows: its status, the release it found, and why it failed.</summary>
internal sealed record UpdateState(UpdateStatus Status, string Version, string Error);

/// <summary>
/// Updates a portable Winshell from its repository's GitHub Releases, for the whole process: every engine's
/// <c>Updates</c> follows the one updater. It finds the latest release, downloads the zip for this architecture,
/// checks its SHA-256 against the digest GitHub reports, and unzips it into a folder of its own under
/// <c>%LOCALAPPDATA%\winshell\updates</c>. <see cref="UpdateInstall"/> applies it when Winshell next starts.
/// </summary>
public sealed class Updater : IDisposable
{
    /// <summary>The first automatic check, once a shell has started.</summary>
    internal static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);

    /// <summary>Automatic checks after the first.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly string _installDir;
    private readonly string _updatesDir;
    private readonly Version _current;
    private readonly string _runtime;
    private readonly Log _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private UpdateState _state;
    private Timer? _timer;
    private bool _automatic;

    internal Updater(string repository, string installDir, string updatesDir, string currentVersion, string runtime, Log log, HttpMessageHandler? handler = null)
    {
        _repository = repository;
        _installDir = installDir;
        _updatesDir = updatesDir;
        _current = ParseVersion(currentVersion) ?? new Version(0, 0, 0);
        _runtime = runtime;
        _log = log;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        // A download on a slow connection takes longer than HttpClient's default 100 seconds.
        _http.Timeout = TimeSpan.FromMinutes(30);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("winshell", _current.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _state = UpdateInstall.Staged(updatesDir, _current) is { } staged
            ? new UpdateState(UpdateStatus.Ready, staged.Version.ToString(3), "")
            : new UpdateState(UpdateStatus.Idle, "", "");
    }

    /// <summary>
    /// The updater for this process, or null when Winshell does not update itself: a build from source, which names
    /// no release repository, or a packaged Winshell, which its installer (the Microsoft Store) updates.
    /// </summary>
    public static Updater? ForRelease(string? repository, string currentVersion, Log log)
    {
        if (string.IsNullOrEmpty(repository) || Packaging.IsPackaged)
            return null;
        return new Updater(repository, AppContext.BaseDirectory, UpdatesDir, currentVersion, RuntimeName, log);
    }

    /// <summary>
    /// Before a shell starts: deletes what the last update left behind and puts a downloaded update in place. Returns
    /// null when there was none to install, or the version and why it did not install, with <c>Error</c> null when it
    /// did. Nothing happens in a Winshell that does not update itself.
    /// </summary>
    public static InstalledUpdate? InstallStaged(string? repository, string currentVersion)
    {
        if (string.IsNullOrEmpty(repository) || Packaging.IsPackaged)
            return null;
        var current = ParseVersion(currentVersion) ?? new Version(0, 0, 0);
        var installDir = AppContext.BaseDirectory;
        UpdateInstall.CleanUp(installDir, UpdatesDir, current);
        if (UpdateInstall.Staged(UpdatesDir, current) is not { } staged)
            return null;
        return new InstalledUpdate(staged.Version.ToString(3), UpdateInstall.ApplyVerified(UpdatesDir, staged.Path, installDir));
    }

    /// <summary>Downloaded updates, each unzipped in a folder named for its version.</summary>
    public static string UpdatesDir => Path.Combine(Winshell.Runtime.Shell.ShellLocations.LocalRoot, "updates");

    /// <summary>The runtime a release zip is built for: <c>win-x64</c> or <c>win-arm64</c>.</summary>
    internal static string RuntimeName => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    /// <summary>Raised on a background thread when <see cref="State"/> changes.</summary>
    internal event Action<UpdateState>? Changed;

    internal UpdateState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>
    /// Starts the shell over, so the update applies, and returns null; or returns why it does not, such as another
    /// Winshell running, whose files the update must not replace. The host that runs the shell sets it.
    /// </summary>
    public Func<string?>? Restart { get; set; }

    /// <summary>
    /// Whether the updater checks by itself, <see cref="FirstCheck"/> after it is turned on and every
    /// <see cref="Interval"/> after that, and downloads what it finds. Off until a shell turns it on.
    /// </summary>
    internal bool Automatic
    {
        get { lock (_lock) return _automatic; }
        set
        {
            lock (_lock)
            {
                if (_automatic == value)
                    return;
                _automatic = value;
                _timer?.Dispose();
                _timer = value ? new Timer(_ => ServiceSupport.Run("updates", CheckAsync), null, FirstCheck, Interval) : null;
            }
        }
    }

    /// <summary>Records an update that downloaded but did not apply when Winshell started.</summary>
    public void ReportApplyFailure(string version, string error) =>
        Set(new UpdateState(UpdateStatus.Failed, version, $"Winshell could not install {version}: {error}"));

    /// <summary>
    /// Finds the latest release and, when it is newer than this Winshell, downloads and stages it. One check runs
    /// at a time; a check asked for while one runs waits for it and then finds the result it left.
    /// </summary>
    internal async Task CheckAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var ready = State.Status == UpdateStatus.Ready ? State.Version : null;
            Set(new UpdateState(UpdateStatus.Checking, ready ?? "", ""));
            Release release;
            try
            {
                release = await LatestAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
            {
                Fail(ready, "Winshell could not check for updates: " + e.Message);
                return;
            }
            if (release.Version <= _current)
            {
                Set(new UpdateState(UpdateStatus.UpToDate, release.Version.ToString(3), ""));
                return;
            }
            if (UpdateInstall.Staged(_updatesDir, _current) is { } staged && staged.Version >= release.Version)
            {
                Set(new UpdateState(UpdateStatus.Ready, staged.Version.ToString(3), ""));
                return;
            }
            var version = release.Version.ToString(3);
            if (!CanWrite(_installDir))
            {
                Fail(null, $"Winshell {version} is out, but Winshell cannot write to {_installDir}. Update it the way you installed it.", version);
                return;
            }
            Set(new UpdateState(UpdateStatus.Downloading, version, ""));
            try
            {
                await DownloadAsync(release).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Fail(null, $"Winshell could not download {version}: {e.Message}", version);
                return;
            }
            _log.Info("updates", $"Winshell {version} is ready. It applies when Winshell next starts.");
            Set(new UpdateState(UpdateStatus.Ready, version, ""));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Starts the shell over to apply the ready update. Returns false when none is ready or no host can. When the host
    /// will not restart, the update stays ready and <see cref="UpdateState.Error"/> says why.
    /// </summary>
    internal bool RestartToUpdate()
    {
        var state = State;
        if (state.Status != UpdateStatus.Ready || Restart is not { } restart)
            return false;
        var refused = restart();
        if (refused is not null)
            _log.Warn("updates", refused);
        Set(state with { Error = refused ?? "" });
        return true;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
        _http.Dispose();
        _gate.Dispose();
    }

    private sealed record Release(Version Version, Uri Url, string Sha256);

    private async Task<Release> LatestAsync()
    {
        using var response = await _http.GetAsync(new Uri($"https://api.github.com/repos/{_repository}/releases/latest")).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag) ?? throw new InvalidDataException($"The latest release, \"{tag}\", is not a version.");
        var suffix = $"-{_runtime}.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("winshell-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            // GitHub computes the digest when the file is uploaded. Without one, there is nothing to check the download against.
            var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{name} has no SHA-256 digest, so Winshell does not install it.");
            var url = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
            if (url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"{name} is not served over HTTPS.");
            return new Release(version, url, digest["sha256:".Length..]);
        }
        // A release that is still uploading its files, or has none for this architecture.
        throw new InvalidDataException($"Release {tag} has no zip for {_runtime}.");
    }

    private async Task DownloadAsync(Release release)
    {
        var version = release.Version.ToString(3);
        Directory.CreateDirectory(_updatesDir);
        // Names of this download's own: another shell may be downloading the same release, and a failure here must
        // not delete what that one unzipped. Neither name reads as a version, so nothing installs them.
        var temp = $"{version}.{Guid.NewGuid():N}";
        var zip = Path.Combine(_updatesDir, temp + ".zip.partial");
        var unzipped = Path.Combine(_updatesDir, temp + ".partial");
        var target = Path.Combine(_updatesDir, version);
        try
        {
            string actual;
            using (var response = await _http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var file = new FileStream(zip, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await body.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                }
                actual = Convert.ToHexStringLower(sha.GetHashAndReset());
            }
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"the file's SHA-256 is {actual}, but the release says {release.Sha256.ToLowerInvariant()}.");
            // ExtractToDirectory refuses an entry that would land outside the folder.
            ZipFile.ExtractToDirectory(zip, unzipped);
            if (!File.Exists(Path.Combine(unzipped, "winshell.exe")))
                throw new InvalidDataException("the zip has no winshell.exe.");
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(unzipped, target);
            // Kept outside the updates folder, so a later start can hash this zip again before it copies anything.
            UpdateTrust.Keep(_updatesDir, version, zip, actual);
        }
        finally
        {
            TryDelete(zip);
            if (Directory.Exists(unzipped))
            {
                try { Directory.Delete(unzipped, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private void Fail(string? ready, string message, string version = "")
    {
        _log.Warn("updates", message);
        // A ready update stays ready when a later check fails: it is still there to apply.
        Set(ready is not null
            ? new UpdateState(UpdateStatus.Ready, ready, "")
            : new UpdateState(UpdateStatus.Failed, version, message));
    }

    private void Set(UpdateState state)
    {
        lock (_lock)
        {
            if (_state == state)
                return;
            _state = state;
        }
        Changed?.Invoke(state);
    }

    private static bool CanWrite(string dir)
    {
        var probe = Path.Combine(dir, $".winshell-write-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads <c>v1.2.3</c> or <c>1.2.3</c>, ignoring a prerelease or build suffix.</summary>
    internal static Version? ParseVersion(string text)
    {
        var core = text.Trim().TrimStart('v', 'V');
        var end = core.IndexOfAny(['-', '+']);
        if (end >= 0)
            core = core[..end];
        var parts = core.Split('.');
        if (parts.Length != 3)
            return null;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }
        return new Version(numbers[0], numbers[1], numbers[2]);
    }
}

/// <summary>Whether this process runs with an MSIX package identity.</summary>
internal static partial class Packaging
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = Check();

    private static unsafe bool Check()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint packageFullNameLength, char* packageFullName);
}
