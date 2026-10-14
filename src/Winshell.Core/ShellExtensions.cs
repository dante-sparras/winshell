namespace Winshell.Core;

/// <summary>Which loaded extension fills the shell's surfaces, when one of them does.</summary>
public static class ShellExtensions
{
    /// <summary>
    /// The extension that fills the surfaces: the first in <paramref name="extensions"/> whose
    /// <see cref="IShellExtension.FillsSurfaces"/> is true. Null when the shell draws its own desk.
    /// </summary>
    /// <param name="extensions">What <see cref="ExtensionLoader.Load"/> returned, in that order.</param>
    /// <returns>The extension, or null.</returns>
    public static IShellExtension? Filler(IReadOnlyList<IShellExtension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (var extension in extensions)
        {
            if (extension.FillsSurfaces)
            {
                return extension;
            }
        }

        return null;
    }
}
