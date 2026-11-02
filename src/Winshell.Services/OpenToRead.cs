using System.ComponentModel;
using System.Diagnostics;

namespace Winshell;

/// <summary>Opens a file so someone can read it, and never runs it.</summary>
internal static class OpenToRead
{
    /// <summary>
    /// Opens a file off the loop, so a slow app does not hold up the shell. A crash report opens with what Windows opens
    /// it with. Any other file opens with its edit verb, since opening a script runs it. A file no app opens that way is
    /// shown selected in File Explorer.
    /// </summary>
    public static void Open(string path, Log log) => _ = Task.Run(() => OpenNow(path, log));

    /// <summary>The verb a file is opened with: null for the default, which only a report gets.</summary>
    internal static string? VerbFor(string path) =>
        Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase) ? null : "edit";

    private static void OpenNow(string path, Log log)
    {
        try
        {
            var open = new ProcessStartInfo(path) { UseShellExecute = true };
            if (VerbFor(path) is { } verb)
                open.Verb = verb;
            Process.Start(open)?.Dispose();
        }
        catch (Win32Exception)
        {
            try
            {
                // Explorer by its full path, not the search path. It reads its own command line: the path is quoted
                // after the comma, not the whole argument.
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(explorer, $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                log.Warn("notice", $"Could not open {path}: {e.Message}");
            }
        }
        catch (InvalidOperationException e)
        {
            log.Warn("notice", $"Could not open {path}: {e.Message}");
        }
    }
}
