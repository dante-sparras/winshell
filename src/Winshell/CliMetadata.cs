using System.Reflection;

namespace Winshell;

internal static class CliMetadata
{
    internal static readonly string Version = typeof(CliMetadata).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}
