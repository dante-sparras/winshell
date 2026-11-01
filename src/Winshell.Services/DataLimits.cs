using System.Globalization;

namespace Winshell;

/// <summary>
/// How much of a file Winshell will read. A preferences file is a dozen keys.
/// <see cref="FileView"/> and <see cref="JsonAdapter"/> use <see cref="MaxReadBytes"/>.
/// TOML, and one QML file in doctor, use <see cref="MaxTextBytes"/>.
/// </summary>
internal static class DataLimits
{
    /// <summary>64 MiB. A general file read, including JSON, stops past this.</summary>
    public const int MaxReadBytes = 64 * 1024 * 1024;

    /// <summary>1 MiB. A TOML file, and one QML file in doctor, stop past this.</summary>
    public const int MaxTextBytes = 1024 * 1024;

    /// <summary>Null when <paramref name="bytes"/> is within <paramref name="maxBytes"/>.</summary>
    public static string? FileBytes(long bytes, int maxBytes) =>
        bytes > maxBytes
            ? string.Create(CultureInfo.InvariantCulture, $"file is {bytes} bytes, above the limit of {maxBytes}")
            : null;

    public static bool IsFileLimit(string message) =>
        message.Contains("above the limit of ", StringComparison.Ordinal);
}
