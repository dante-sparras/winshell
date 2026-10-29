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
    [property: JsonPropertyName("log")] string Log);

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

    public static void Remove(string id)
    {
        try
        {
            File.Delete(Path.Combine(DirOf(id), "instance.json"));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Every instance that wrote its file, newest first. Dead ones are skipped unless asked for.</summary>
    public static List<(InstanceInfo Info, bool Alive)> List(bool includeDead)
    {
        var result = new List<(InstanceInfo, bool)>();
        if (!Directory.Exists(RunDir))
            return result;
        foreach (var dir in Directory.EnumerateDirectories(RunDir))
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
            var alive = IsAlive(info.Pid);
            if (alive || includeDead)
                result.Add((info, alive));
        }
        return result.OrderByDescending(r => r.Item1.LaunchTime).ToList();
    }

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

    /// <summary>Removes the folders of instances that are no longer running, keeping their logs a day.</summary>
    public static void Clean()
    {
        foreach (var (info, alive) in List(includeDead: true))
        {
            if (alive)
                continue;
            var dir = DirOf(info.Id);
            if (DateTime.Now - info.LaunchTime > TimeSpan.FromDays(1))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
