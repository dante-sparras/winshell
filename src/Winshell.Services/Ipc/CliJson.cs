using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winshell;

/// <summary>One running instance as <c>winshell list --json</c> prints it. The instance file has its own type.</summary>
internal sealed record InstanceListItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("pid")] int Pid,
    [property: JsonPropertyName("shellId")] string ShellId,
    [property: JsonPropertyName("configPath")] string ConfigPath,
    [property: JsonPropertyName("launchTime")] DateTime LaunchTime,
    [property: JsonPropertyName("pipe")] string Pipe,
    [property: JsonPropertyName("log")] string Log)
{
    public static InstanceListItem From(InstanceInfo info) =>
        new(info.Id, info.Pid, info.ShellId, info.ConfigPath, info.LaunchTime, info.Pipe, info.Log);
}

[JsonSerializable(typeof(List<InstanceListItem>))]
[JsonSerializable(typeof(IReadOnlyList<IpcTargetView>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CliJsonContext : JsonSerializerContext;

/// <summary>The JSON <c>list --json</c> and <c>ipc show -j</c> write. Escaping matches <c>doctor --json</c>.</summary>
internal static class CliJson
{
    public static string Instances(IEnumerable<InstanceInfo> instances) =>
        Write(instances.Select(InstanceListItem.From).ToList());

    public static string IpcShow(IReadOnlyList<IpcTargetView> targets) =>
        Write(targets);

    /// <summary>The writer is <see cref="JsonText.IndentedWriter"/>, the same one <c>doctor --json</c> uses.</summary>
    private static string Write<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, JsonText.IndentedWriter))
            JsonSerializer.Serialize(json, value, CliJsonContext.Default.Options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
