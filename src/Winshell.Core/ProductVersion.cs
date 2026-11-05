using System.Globalization;
using System.Reflection;

namespace Winshell;

/// <summary>
/// The Winshell release a build reports. The number is the assembly informational version,
/// which the build stamps from the release-please manifest.
/// </summary>
internal sealed class ProductVersion
{
    /// <summary>This assembly's release.</summary>
    public static ProductVersion Current { get; } = Parse(
        typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion);

    private ProductVersion(int major, int minor, int patch, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Text = text;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The informational version text this was parsed from, or <c>0.0.0</c> when it was missing.</summary>
    public string Text { get; }

    /// <summary>Reads major.minor.patch from an informational version, ignoring a prerelease and build id.</summary>
    public static ProductVersion Parse(string? informational)
    {
        var text = string.IsNullOrWhiteSpace(informational) ? "0.0.0" : informational.Trim();
        var core = text;
        var plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            core = core[..plus];
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
            core = core[..dash];
        var parts = core.Split('.');
        if (parts.Length == 0 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) || major < 0)
            return new ProductVersion(0, 0, 0, text);
        return new ProductVersion(major, Part(parts, 1), Part(parts, 2), text);
    }

    private static int Part(string[] parts, int index)
    {
        return index < parts.Length &&
               int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0
            ? n
            : 0;
    }
}
