using Winshell.Native;

namespace Winshell;

/// <summary>Whether this process runs with an MSIX package identity.</summary>
internal static class Packaging
{
    public static bool IsPackaged { get; } = PackageIdentity.Exists();
}
