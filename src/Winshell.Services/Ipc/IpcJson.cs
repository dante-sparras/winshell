using System.Text.Json.Serialization;

namespace Winshell;

internal sealed partial record IpcRequest(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("target")] string? Target = null,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("args")] List<string>? Args = null,
    [property: JsonPropertyName("json"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Json = false,
    [property: JsonPropertyName("protocol"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Protocol = 0,
    [property: JsonPropertyName("winshell"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Winshell = null);

internal sealed record IpcResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("protocol"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Protocol = 0,
    [property: JsonPropertyName("winshell"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Winshell = null);

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
internal sealed partial class IpcJson : JsonSerializerContext;
