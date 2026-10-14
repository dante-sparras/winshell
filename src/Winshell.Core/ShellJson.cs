using System.Text.Json.Serialization;

namespace Winshell.Core;

// The host writes its own JSON from these contracts. The options match the contexts the product still uses for settings.json,
// so a bar copy and a catalog list stay the same text.

/// <summary>Indented, camelCase: the plugin catalog list.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<CatalogPlugin>))]
internal sealed partial class ShellIndentedJson : JsonSerializerContext;

/// <summary>Compact, camelCase, read without regard to case: the bar editor's private copy.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BarLayoutDocument))]
internal sealed partial class ShellLooseJson : JsonSerializerContext;
