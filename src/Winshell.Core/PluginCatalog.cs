using System.Text.Json;

namespace Winshell.Core;

/// <summary>One plugin the Plugin catalog points at.</summary>
/// <param name="Url">The git address <c>winoma plugin add</c> clones.</param>
/// <param name="Id">The id the plugin's manifest declares, which marks an entry the Host already has.</param>
/// <param name="Name">What the Menu calls it.</param>
/// <param name="Description">One line about what it does.</param>
public sealed record CatalogPlugin(string Url, string Id, string Name, string Description);

/// <summary>
/// The Plugin catalog: the plugins Winoma ships a pointer to. Adding one is <c>winoma plugin add</c> with its address, and it stays off until enabled.
/// Only repositories that exist, and that the Host's owner named, are in <see cref="Shipped"/>.
/// </summary>
public sealed class PluginCatalog
{
    /// <summary>Creates a catalog.</summary>
    /// <param name="entries">The plugins it lists, in the order the Menu shows them.</param>
    public PluginCatalog(IReadOnlyList<CatalogPlugin> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
    }

    /// <summary>The list that ships with Winoma. It is empty until a repository is named.</summary>
    public static PluginCatalog Shipped { get; } = new([]);

    /// <summary>The plugins it lists.</summary>
    public IReadOnlyList<CatalogPlugin> Entries { get; }

    /// <summary>Reads a catalog from its text.</summary>
    /// <param name="json">What <see cref="Format"/> wrote.</param>
    /// <returns>The catalog, or an empty one for text that is not a list.</returns>
    public static PluginCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var entries = JsonSerializer.Deserialize(json, ShellIndentedJson.Default.ListCatalogPlugin);
            return new PluginCatalog(entries?.Where(entry => entry is not null && entry.Url is not null && entry.Id is not null).ToList() ?? []);
        }
        catch (JsonException)
        {
            return new PluginCatalog([]);
        }
    }

    /// <summary>The catalog as text.</summary>
    /// <returns>A JSON list of its entries.</returns>
    public string Format() => JsonSerializer.Serialize(Entries.ToList(), ShellIndentedJson.Default.ListCatalogPlugin);
}
