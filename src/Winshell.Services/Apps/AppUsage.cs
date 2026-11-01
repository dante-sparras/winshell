using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Winshell.Apps;

/// <summary>How often and how recently Windows has run an app. <see cref="LastUsedUnixMilliseconds"/> is 0 when unknown.</summary>
internal readonly record struct AppUsage(int RunCount, long LastUsedUnixMilliseconds)
{
    public static AppUsage Merge(AppUsage left, AppUsage right) =>
        new(Math.Max(left.RunCount, right.RunCount), Math.Max(left.LastUsedUnixMilliseconds, right.LastUsedUnixMilliseconds));

    /// <summary>The usage recorded for an app's id, target, shortcut, or program name. Keys are matched ignoring case.</summary>
    public static AppUsage Lookup(IReadOnlyDictionary<string, AppUsage> usage, AppRecord app)
    {
        var best = default(AppUsage);
        void Take(string key)
        {
            if (key.Length > 0 && usage.TryGetValue(key, out var found))
                best = Merge(best, found);
        }
        Take(app.AppId);
        Take(app.Id);
        Take(app.Target);
        Take(app.Icon);
        Take(app.LaunchPath);
        if (app.Target.Length > 0)
            Take(Path.GetFileName(app.Target));
        return best;
    }
}

/// <summary>
/// Recent and frequent use as Windows records it: FeatureUsage launch and switch counts, and UserAssist run count
/// and last run. The higher of the two FeatureUsage counts is kept, so a launch is not added to a switch.
/// </summary>
internal static partial class AppUsageReader
{
    private const string FeatureUsage = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage";
    private const string UserAssist = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";

    public static Dictionary<string, AppUsage> Read()
    {
        var launches = ReadCounts(FeatureUsage + @"\AppLaunch");
        var switches = ReadCounts(FeatureUsage + @"\AppSwitched");
        var assist = ReadUserAssist();
        return From(launches, switches, assist);
    }

    public static Dictionary<string, AppUsage> From(
        IEnumerable<KeyValuePair<string, int>> launches,
        IEnumerable<KeyValuePair<string, int>> switches,
        IEnumerable<(string EncodedName, byte[] Data)> userAssist)
    {
        var usage = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, count) in launches)
            Add(usage, key, new AppUsage(count, 0));
        foreach (var (key, count) in switches)
            Add(usage, key, new AppUsage(count, 0));
        foreach (var (encoded, data) in userAssist)
        {
            var name = Rot13(encoded);
            if (name.StartsWith("UEME_", StringComparison.Ordinal))
                continue;
            if (!TryParse(data, out var parsed))
                continue;
            Add(usage, name, parsed);
        }
        return usage;
    }

    public static string Rot13(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= 'a' and <= 'z')
                chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z')
                chars[i] = (char)('A' + (c - 'A' + 13) % 26);
        }
        return new string(chars);
    }

    /// <summary>UserAssist stores some paths as <c>{known folder guid}\rest</c>. Other strings are returned as given.</summary>
    public static string ExpandKnownFolder(string path)
    {
        if (path.Length < 3 || path[0] != '{')
            return path;
        var end = path.IndexOf('}');
        if (end <= 0 || !Guid.TryParse(path[..(end + 1)], out var id))
            return path;
        var folder = KnownFolder(id);
        if (folder.Length == 0)
            return path;
        var rest = path[(end + 1)..].TrimStart('\\', '/');
        return rest.Length == 0 ? folder : Path.Combine(folder, rest);
    }

    public static bool TryParse(byte[] data, out AppUsage usage)
    {
        usage = default;
        if (data.Length < 16)
            return false;
        int count;
        long fileTime;
        if (data.Length >= 68)
        {
            count = BitConverter.ToInt32(data, 4);
            fileTime = BitConverter.ToInt64(data, 60);
        }
        else
        {
            count = BitConverter.ToInt32(data, 0);
            fileTime = BitConverter.ToInt64(data, 8);
        }
        if (count < 0)
            count = 0;
        var last = 0L;
        if (fileTime > 0)
        {
            try
            {
                var time = DateTimeOffset.FromFileTime(fileTime);
                if (time.Year >= 2000 && time < TimeProvider.System.GetUtcNow().AddDays(2))
                    last = time.ToUnixTimeMilliseconds();
            }
            catch (ArgumentOutOfRangeException)
            {
                last = 0;
            }
        }
        if (count == 0 && last == 0)
            return false;
        usage = new AppUsage(count, last);
        return true;
    }

    private static void Add(Dictionary<string, AppUsage> usage, string key, AppUsage value)
    {
        if (string.IsNullOrWhiteSpace(key) || (value.RunCount <= 0 && value.LastUsedUnixMilliseconds <= 0))
            return;
        var expanded = ExpandKnownFolder(key.Trim());
        Put(usage, expanded, value);
        if (!expanded.Equals(key, StringComparison.OrdinalIgnoreCase))
            Put(usage, key.Trim(), value);
        var file = Path.GetFileName(expanded);
        if (file.Length > 0 && !file.Equals(expanded, StringComparison.OrdinalIgnoreCase))
            Put(usage, file, value);
    }

    private static void Put(Dictionary<string, AppUsage> usage, string key, AppUsage value)
    {
        usage[key] = usage.TryGetValue(key, out var existing) ? AppUsage.Merge(existing, value) : value;
    }

    private static List<KeyValuePair<string, int>> ReadCounts(string subKey)
    {
        var counts = new List<KeyValuePair<string, int>>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            if (key is null)
                return counts;
            foreach (var name in key.GetValueNames())
            {
                if (TryCount(key.GetValue(name), out var count) && count > 0)
                    counts.Add(new KeyValuePair<string, int>(name, count));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return counts;
        }
        return counts;
    }

    private static bool TryCount(object? value, out int count)
    {
        switch (value)
        {
            case int number:
                count = number;
                return true;
            case long number when number is >= 0 and <= int.MaxValue:
                count = (int)number;
                return true;
            default:
                count = 0;
                return false;
        }
    }

    private static List<(string EncodedName, byte[] Data)> ReadUserAssist()
    {
        var entries = new List<(string, byte[])>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(UserAssist);
            if (root is null)
                return entries;
            foreach (var guid in root.GetSubKeyNames())
            {
                using var count = root.OpenSubKey(guid + @"\Count");
                if (count is null)
                    continue;
                foreach (var name in count.GetValueNames())
                {
                    if (count.GetValue(name) is byte[] data)
                        entries.Add((name, data));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return entries;
        }
        return entries;
    }

    private static string KnownFolder(Guid id)
    {
        var folder = id;
        if (SHGetKnownFolderPath(in folder, 0, 0, out var path) != 0 || path == 0)
            return "";
        try
        {
            return Marshal.PtrToStringUni(path) ?? "";
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
