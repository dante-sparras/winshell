using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winshell;

/// <summary>What a running instance writes about itself, so the CLI can find and reach it.</summary>
internal sealed record InstanceInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("shellId")]
    string ShellId,
    [property: JsonPropertyName("configPath")]
    string ConfigPath,
    [property: JsonPropertyName("launchTime")]
    DateTime LaunchTime,
    [property: JsonPropertyName("pipe")] string Pipe,
    [property: JsonPropertyName("log")] string Log,
    // UTC ticks of Process.StartTime. Windows reuses process ids, so the pid alone is not this process.
    [property: JsonPropertyName("processStart")]
    long ProcessStart = 0,
    // Set when the shell exits cleanly. The folder stays so `winshell log` can still find it.
    [property: JsonPropertyName("exitedAt")]
    DateTime? ExitedAt = null,
    // The process's name, since a configuration's program can have any.
    [property: JsonPropertyName("processName")]
    string? ProcessName = null);

[JsonSerializable(typeof(InstanceInfo))]
[JsonSerializable(typeof(List<InstanceInfo>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class InstanceJson : JsonSerializerContext;

/// <summary>
/// The instances in one folder, <see cref="ShellLocations.RunRoot"/>, each in a folder of its own that holds its
/// record, <see cref="RecordName"/>, and its log, <see cref="InstanceLog.FileName"/>.
/// </summary>
internal sealed class Instances(string folder)
{
    public const string RecordName = "instance.json";

    public static string NewId()
    {
        return Convert.ToHexStringLower(Guid.NewGuid().ToByteArray())[..12];
    }

    public string DirOf(string id)
    {
        return Path.Combine(folder, id);
    }

    public void Write(InstanceInfo info)
    {
        var dir = DirOf(info.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, RecordName),
            JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo));
    }

    /// <summary>
    /// A clean exit keeps <c>instance.json</c>, marked with <see cref="InstanceInfo.ExitedAt"/>, so the log
    /// can still be found. <see cref="Clean"/> deletes the folder once it is a day old.
    /// </summary>
    public void Remove(string id)
    {
        if (Read(DirOf(id)) is not { ExitedAt: null } info)
            return;
        try
        {
            Write(info with { ExitedAt = TimeProvider.System.GetLocalNow().LocalDateTime });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Every instance that wrote its file, newest first. Dead ones are skipped unless asked for.</summary>
    public List<(InstanceInfo Info, bool Alive)> List(bool includeDead)
    {
        var result = new List<(InstanceInfo Info, bool Alive)>();
        if (!Directory.Exists(folder))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            if (Read(dir) is not { } info)
                continue;
            var alive = IsRunning(info);
            if (alive || includeDead)
                result.Add((info, alive));
        }

        // Two files can name one live process only when this user cannot read its start time. The newest is its own.
        return result.Where(row => !row.Alive)
            .Concat(result.Where(row => row.Alive).GroupBy(row => row.Info.Pid)
                .Select(group => group.MaxBy(row => row.Info.LaunchTime)))
            .OrderByDescending(row => row.Info.LaunchTime).ToList();
    }

    /// <summary>The record in an instance's folder, or null when it has none that can be read.</summary>
    private static InstanceInfo? Read(string dir)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(dir, RecordName)),
                InstanceJson.Default.InstanceInfo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The instance has not exited and its process id still names the process that wrote it. A killed instance leaves
    /// its file for a day, and Windows can give that process id to the next shell.
    /// </summary>
    internal static bool IsRunning(InstanceInfo info)
    {
        if (info.ExitedAt is not null)
            return false;
        try
        {
            using var process = Process.GetProcessById(info.Pid);
            return Wrote(info, process);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="process"/> wrote the record, as far as this user can tell. This user may not be able
    /// to open the process, so the name is read first: reading it needs no handle. A process whose start time cannot
    /// be read is taken at its name.
    /// </summary>
    internal static bool Wrote(InstanceInfo info, Process process)
    {
        try
        {
            if (!string.Equals(process.ProcessName, info.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                process.HasExited)
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

        try
        {
            return process.StartTime.ToUniversalTime().Ticks == info.ProcessStart;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Waits for the shell a new one takes over from to let go of the taskbar, the hotkeys and the tray.</summary>
    internal static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // It has already gone.
        }
    }

    /// <summary>Removes the folders of instances that are no longer running, keeping their logs a day.</summary>
    /// <remarks>
    /// A stopped instance's folder goes once it is a day old, measured from when it exited, or else from the log's last
    /// write. A folder with no record that can be read, from a start that ended before it wrote one, goes on the same
    /// rule.
    /// </remarks>
    public void Clean()
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            var info = Read(dir);
            if (info is not null && IsRunning(info))
                continue;
            if (TimeProvider.System.GetLocalNow().LocalDateTime - LastUsed(dir, info) > TimeSpan.FromDays(1))
                TryDelete(dir);
        }
    }

    private static DateTime LastUsed(string dir, InstanceInfo? info)
    {
        if (info?.ExitedAt is { } exited)
            return exited;
        var log = Path.Combine(dir, InstanceLog.FileName);
        if (File.Exists(log))
            return File.GetLastWriteTime(log);
        return info?.LaunchTime ?? Directory.GetLastWriteTime(dir);
    }

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
