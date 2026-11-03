using System.Reflection;

namespace Winshell;

internal static class CliMetadata
{
    internal static readonly string Version = typeof(CliMetadata).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>
    /// The GitHub repository a release build was published from, as <c>scripts/package.ps1</c> stamps it. A build
    /// from source has none, so it never updates itself.
    /// </summary>
    internal static readonly string? ReleaseRepository = typeof(CliMetadata).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "WinshellReleaseRepository")?.Value;
}
