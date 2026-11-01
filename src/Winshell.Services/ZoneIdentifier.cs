using System.Globalization;

namespace Winshell;

/// <summary>
/// The zone Windows records for a downloaded file, in its <c>Zone.Identifier</c> stream. Browsers, mail and
/// Explorer's zip extraction write it; Properties > Unblock and <c>Unblock-File</c> remove it.
/// </summary>
public static class ZoneIdentifier
{
    /// <summary>Whether a file came from the Internet (3) or Restricted (4) zone. A file without the stream did not.</summary>
    public static bool IsDownloaded(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path + ":Zone.Identifier");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        foreach (var line in text.Split('\n'))
        {
            var entry = line.Trim();
            if (entry.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(entry[7..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zone) && zone >= 3;
        }
        return false;
    }

    /// <summary>Why a downloaded configuration does not run, for the notice and the log.</summary>
    public static string Blocked(string rootFile) =>
        $"{Path.GetFileName(rootFile)} was downloaded from the internet. It runs as you. If you trust it, unblock it (right-click > Properties > Unblock, or Unblock-File) and save it to load it.";
}
