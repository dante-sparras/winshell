using System.Diagnostics;
using System.Runtime.InteropServices;
using Winshell.Native;

namespace Winshell.Media;

/// <summary>The name Windows shows for the app behind a media session, found from its AppUserModelID.</summary>
internal static class AppDisplayName
{
    /// <summary>
    /// The name the Start menu shows for an AppUserModelID, or the program name when it has none. It asks the
    /// shell and can scan processes, so call it off the UI thread.
    /// </summary>
    public static string Find(string appId)
    {
        var name = StartMenuName(appId);
        if (string.IsNullOrWhiteSpace(name) && IsProgram(appId))
            name = ProgramDescription(appId[..^4]);
        return string.IsNullOrWhiteSpace(name) ? Fallback(appId) : name;
    }

    /// <summary>The exe or app id, before <see cref="Find"/> replaces it.</summary>
    public static string Fallback(string appId) => IsProgram(appId) ? appId[..^4] : appId;

    private static bool IsProgram(string appId) => appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name the Start menu shows for an AppUserModelID.</summary>
    private static string? StartMenuName(string appId)
    {
        if (appId.Length == 0)
            return null;
        var iid = typeof(IShellItem).GUID;
        if (SHCreateItemFromParsingName($"shell:AppsFolder\\{appId}", 0, ref iid, out var item) != 0)
            return null;
        try
        {
            return item.GetDisplayName(0, out var name) == 0 ? name : null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out IShellItem item);

    private static string? ProgramDescription(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                        return description.Trim();
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
        }
        return null;
    }
}
