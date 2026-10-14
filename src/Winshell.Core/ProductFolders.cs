namespace Winshell.Core;

/// <summary>
/// Where the products sit on disk. Winland, Winshell, and Winoma each install into a folder of their own, side by side,
/// and a build lays them out the same way (ADR 0176).
/// </summary>
public static class ProductFolders
{
    /// <summary>The folder that holds a product's files.</summary>
    public const string Winland = "Winland";

    /// <summary>The folder that holds the shell's files.</summary>
    public const string Winshell = "Winshell";

    /// <summary>The folder that holds Winoma's files.</summary>
    public const string Winoma = "Winoma";

    /// <summary>The folder of <paramref name="product"/> beside the product folder <paramref name="folder"/>.</summary>
    /// <param name="folder">One product's folder, such as the running program's.</param>
    /// <param name="product">The other product's folder name, such as <see cref="Winland"/>.</param>
    /// <returns>The sibling folder. It may not exist.</returns>
    public static string Sibling(string folder, string product)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(product);
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var parent = Path.GetDirectoryName(trimmed);
        return parent is null ? Path.Combine(trimmed, product) : Path.Combine(parent, product);
    }

    /// <summary>
    /// The folder of built-in plugins: the <c>plugins</c> folder beside the first extension that has one, else the one beside the shell.
    /// A shell with no extension ships no widgets, so its own folder is usually empty.
    /// </summary>
    /// <param name="baseDirectory">The shell's folder.</param>
    /// <param name="extensionFolders">The folders of the loaded extensions, in load order.</param>
    /// <param name="exists">Whether a folder exists. Tests pass their own.</param>
    /// <returns>The folder to scan for built-in plugins.</returns>
    public static string BuiltInPlugins(string baseDirectory, IReadOnlyList<string> extensionFolders, Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(extensionFolders);
        exists ??= Directory.Exists;
        foreach (var folder in extensionFolders)
        {
            var plugins = Path.Combine(folder, "plugins");
            if (exists(plugins))
            {
                return plugins;
            }
        }

        return Path.Combine(baseDirectory, "plugins");
    }
}
