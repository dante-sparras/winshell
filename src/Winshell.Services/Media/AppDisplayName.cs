using System.ComponentModel;
using System.Diagnostics;
using Winshell.Apps;
using Winshell.Native;

namespace Winshell.Media;

/// <summary>The name Windows shows for the app behind a media session, found from its AppUserModelID.</summary>
internal static class AppDisplayName
{
    /// <summary>
    /// The name the Start menu shows for an AppUserModelID, or else the running program's description, or else
    /// <see cref="AppIdentity.FallbackName"/>. It asks the shell and can scan processes, so call it off the UI thread.
    /// </summary>
    public static string Find(string appId)
    {
        var name = appId.Length == 0 ? null : ShellItems.Name(AppIdentity.AppsFolderPath(appId));
        if (string.IsNullOrWhiteSpace(name) && AppIdentity.ProgramName(appId) is { } program)
            name = RunningProgramDescription(program);
        return string.IsNullOrWhiteSpace(name) ? AppIdentity.FallbackName(appId) : name;
    }

    private static string? RunningProgramDescription(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path &&
                        AppIdentity.ProgramDescription(path) is { } description)
                        return description;
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                }
            }

        return null;
    }
}
