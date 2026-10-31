using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

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

/// <summary>A function or signal parameter as <c>ipc show -j</c> prints it.</summary>
internal sealed record IpcShowParameter(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type);

/// <summary>A function as <c>ipc show -j</c> prints it.</summary>
internal sealed record IpcShowFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parameters")] IReadOnlyList<IpcShowParameter> Parameters,
    [property: JsonPropertyName("returns")] string Returns);

/// <summary>A property as <c>ipc show -j</c> prints it.</summary>
internal sealed record IpcShowProperty(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type);

/// <summary>A signal as <c>ipc show -j</c> prints it.</summary>
internal sealed record IpcShowSignal(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parameters")] IReadOnlyList<IpcShowParameter> Parameters);

/// <summary>One IPC target as <c>ipc show -j</c> prints it.</summary>
internal sealed record IpcShowTarget(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("functions")] IReadOnlyList<IpcShowFunction> Functions,
    [property: JsonPropertyName("properties")] IReadOnlyList<IpcShowProperty> Properties,
    [property: JsonPropertyName("signals")] IReadOnlyList<IpcShowSignal> Signals)
{
    public static IpcShowTarget From(IpcTargetView view) => new(
        view.Target,
        view.Functions.Select(f => new IpcShowFunction(f.Name, f.Parameters.Select(p => new IpcShowParameter(p.Name, p.Type)).ToList(), f.Returns)).ToList(),
        view.Properties.Select(p => new IpcShowProperty(p.Name, p.Type)).ToList(),
        view.Signals.Select(s => new IpcShowSignal(s.Name, s.Parameters.Select(p => new IpcShowParameter(p.Name, p.Type)).ToList())).ToList());
}

[JsonSerializable(typeof(List<InstanceListItem>))]
[JsonSerializable(typeof(List<IpcShowTarget>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class CliJsonContext : JsonSerializerContext;

/// <summary>The JSON <c>list --json</c> and <c>ipc show -j</c> write. Escaping matches <c>doctor --json</c>.</summary>
internal static class CliJson
{
    public static string Instances(IEnumerable<InstanceInfo> instances) =>
        Write(instances.Select(InstanceListItem.From).ToList());

    public static string IpcShow(IReadOnlyList<IpcTargetView> targets) =>
        Write(targets.Select(IpcShowTarget.From).ToList());

    /// <summary>The writer is <see cref="JsonText.IndentedWriter"/>, the same one <c>doctor --json</c> uses.</summary>
    private static string Write<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, JsonText.IndentedWriter))
            JsonSerializer.Serialize(json, value, CliJsonContext.Default.Options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
