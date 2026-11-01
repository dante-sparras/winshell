using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winshell.Runtime.Shell;

namespace Winshell;

/// <summary>What a running instance writes about itself, so the CLI can find and reach it.</summary>
internal sealed record InstanceInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("shellId")] string ShellId,
    [property: JsonPropertyName("configPath")] string ConfigPath,
    [property: JsonPropertyName("launchTime")] DateTime LaunchTime,
    [property: JsonPropertyName("pipe")] string Pipe,
    [property: JsonPropertyName("log")] string Log,
    // UTC ticks of Process.StartTime. Windows reuses process ids, so the pid alone is not this process.
    // Files written before this field existed leave it at 0.
    [property: JsonPropertyName("processStart")] long ProcessStart = 0,
    // Set when the shell exits cleanly. The folder stays so `winshell log` can still find it.
    [property: JsonPropertyName("exitedAt")] DateTime? ExitedAt = null);

[JsonSerializable(typeof(InstanceInfo))]
[JsonSerializable(typeof(List<InstanceInfo>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class InstanceJson : JsonSerializerContext;

/// <summary>The running instances, each a folder under %LOCALAPPDATA%\winshell\run. The log is log.txt in that folder.</summary>
internal static class Instances
{
    public static string RunDir => ShellLocations.RunRoot;

    public static string NewId() => Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())[..12];

    public static string DirOf(string id) => Path.Combine(RunDir, id);

    public static void Write(InstanceInfo info)
    {
        var dir = DirOf(info.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "instance.json"), JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo));
    }

    /// <summary>
    /// A clean exit keeps <c>instance.json</c>, marked with <see cref="InstanceInfo.ExitedAt"/>, so the log
    /// can still be found. <see cref="Clean"/> deletes the folder once it is a day old.
    /// </summary>
    public static void Remove(string id) => MarkExited(RunDir, id);

    internal static void MarkExited(string runDir, string id)
    {
        var file = Path.Combine(runDir, id, "instance.json");
        try
        {
            var info = JsonSerializer.Deserialize(File.ReadAllText(file), InstanceJson.Default.InstanceInfo);
            if (info is null)
                return;
            if (info.ExitedAt is not null)
                return;
            File.WriteAllText(file, JsonSerializer.Serialize(info with { ExitedAt = TimeProvider.System.GetLocalNow().LocalDateTime }, InstanceJson.Default.InstanceInfo));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Every instance that wrote its file, newest first. Dead ones are skipped unless asked for.</summary>
    public static List<(InstanceInfo Info, bool Alive)> List(bool includeDead) => ListIn(RunDir, includeDead);

    internal static List<(InstanceInfo Info, bool Alive)> ListIn(string runDir, bool includeDead)
    {
        var result = new List<(InstanceInfo, bool)>();
        if (!Directory.Exists(runDir))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(runDir))
        {
            var file = Path.Combine(dir, "instance.json");
            if (!File.Exists(file))
                continue;
            InstanceInfo? info;
            try
            {
                info = JsonSerializer.Deserialize(File.ReadAllText(file), InstanceJson.Default.InstanceInfo);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                continue;
            }
            if (info is null)
                continue;
            // A clean exit leaves the file in place, with exitedAt set, so it is not a running instance.
            var alive = info.ExitedAt is null && IsCurrent(info);
            if (alive || includeDead)
                result.Add((info, alive));
        }
        if (result.Count > 1)
            result = OneRowPerProcess(result);
        return result.OrderByDescending(r => r.Item1.LaunchTime).ToList();
    }

    /// <summary>
    /// The process id belongs to a running winshell. Callers that only have a pid cannot tell which
    /// incarnation it is; <see cref="IsCurrent"/> can, and that is what <see cref="List"/> uses.
    /// </summary>
    internal static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // Windows reuses process ids, and this user may not be able to open the process that has the id now,
            // so the name is read first: reading it needs no handle.
            return p.ProcessName.StartsWith("winshell", StringComparison.OrdinalIgnoreCase) && !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // A winshell this user cannot open, such as one running elevated, is still running.
            return true;
        }
    }

    /// <summary>
    /// The file belongs to the winshell that has this process id now. A killed instance leaves its file for a
    /// day, and Windows can give that process id to the next winshell; the old file then names the new process.
    /// </summary>
    private static bool IsCurrent(InstanceInfo info)
    {
        try
        {
            using var process = Process.GetProcessById(info.Pid);
            try
            {
                if (!process.ProcessName.StartsWith("winshell", StringComparison.OrdinalIgnoreCase) || process.HasExited)
                    return false;
            }
            catch (Win32Exception)
            {
                return true;
            }
            var started = StartTicks(process);
            if (started == 0)
                return true;
            if (info.ProcessStart != 0)
                return info.ProcessStart == started;
            // A file from before processStart was recorded was written with DateTime.Now, after the process
            // existed. One whose launch is earlier belongs to the previous owner of this process id.
            return AsUtc(info.LaunchTime) >= new DateTime(started, DateTimeKind.Utc).AddMilliseconds(-250);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    /// <summary>One row per live process id. Two files can both name it; the one this process wrote is kept.</summary>
    private static List<(InstanceInfo Info, bool Alive)> OneRowPerProcess(List<(InstanceInfo Info, bool Alive)> items)
    {
        var kept = new List<(InstanceInfo, bool)>();
        foreach (var group in items.Where(i => i.Alive).GroupBy(i => i.Info.Pid))
            kept.Add((Choose(group.Select(i => i.Info).ToList()), true));
        foreach (var item in items)
        {
            if (!item.Alive)
                kept.Add(item);
        }
        return kept;
    }

    private static InstanceInfo Choose(List<InstanceInfo> samePid)
    {
        var started = StartTicks(samePid[0].Pid);
        if (started != 0)
        {
            var exact = samePid.Where(i => i.ProcessStart == started).ToList();
            if (exact.Count > 0)
                return exact.OrderByDescending(i => i.LaunchTime).First();
        }
        return samePid.OrderByDescending(i => i.LaunchTime).First();
    }

    private static long StartTicks(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return StartTicks(process);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return 0;
        }
    }

    private static long StartTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or ArgumentException)
        {
            return 0;
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
    };

    /// <summary>Removes the folders of instances that are no longer running, keeping their logs a day.</summary>
    public static void Clean() => CleanIn(RunDir);

    /// <summary>
    /// Deletes a stopped instance's folder once it is a day old, measured from when it exited, or from the log's
    /// last write when it left no exit time. A folder with no <c>instance.json</c> is deleted on the same rule,
    /// so a clean exit from an older build does not leave a folder forever.
    /// </summary>
    internal static void CleanIn(string runDir)
    {
        if (!Directory.Exists(runDir))
            return;
        foreach (var dir in Directory.EnumerateDirectories(runDir))
        {
            var file = Path.Combine(dir, "instance.json");
            if (!File.Exists(file))
            {
                var log = Path.Combine(dir, "log.txt");
                var stamp = File.Exists(log) ? File.GetLastWriteTime(log) : Directory.GetLastWriteTime(dir);
                if (OlderThanADay(stamp))
                    TryDelete(dir);
                continue;
            }
            InstanceInfo? info;
            try
            {
                info = JsonSerializer.Deserialize(File.ReadAllText(file), InstanceJson.Default.InstanceInfo);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                continue;
            }
            if (info is null || (info.ExitedAt is null && IsCurrent(info)))
                continue;
            if (OlderThanADay(AgeOf(dir, info)))
                TryDelete(dir);
        }
    }

    private static DateTime AgeOf(string dir, InstanceInfo info)
    {
        if (info.ExitedAt is { } exited)
            return exited;
        var log = Path.Combine(dir, "log.txt");
        return File.Exists(log) ? File.GetLastWriteTime(log) : info.LaunchTime;
    }

    private static bool OlderThanADay(DateTime stamp) => TimeProvider.System.GetLocalNow().LocalDateTime - stamp > TimeSpan.FromDays(1);

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
