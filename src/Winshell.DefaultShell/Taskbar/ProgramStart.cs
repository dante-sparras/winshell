using System.ComponentModel;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Starts a program pinned by its path, or another window of a running program the app index does not have.</summary>
internal static class ProgramStart
{
    public static void Start(string path, Log log)
    {
        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            log.Warn("taskbar", $"Could not start \"{path}\": {e.Message}");
        }
    }
}
