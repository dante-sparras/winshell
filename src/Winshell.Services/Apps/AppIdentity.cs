using System.ComponentModel;
using System.Diagnostics;

namespace Winshell.Apps;

/// <summary>
/// Which app an AppUserModelID names, and the name to show for it. A packaged app's id contains <c>!</c>, and a
/// desktop program's id can be its file name, such as <c>Spotify.exe</c>.
/// </summary>
internal static class AppIdentity
{
    /// <summary>Whether the id is a packaged (Store) app's.</summary>
    public static bool IsPackaged(string appId)
    {
        return appId.Contains('!');
    }

    /// <summary>The shell path Start opens and draws the app by, and the shell names it by.</summary>
    public static string AppsFolderPath(string appId)
    {
        return @"shell:AppsFolder\" + appId;
    }

    /// <summary>
    /// Opens the app through its <see cref="AppsFolderPath"/>, as Start does. Null when Windows took it, otherwise
    /// why not.
    /// </summary>
    public static string? OpenAppsFolder(string appId)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", AppsFolderPath(appId))
                { UseShellExecute = true });
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return e.Message;
        }
    }

    /// <summary>The program's name without <c>.exe</c> when the id is a program's file name, otherwise null.</summary>
    public static string? ProgramName(string appId)
    {
        return !IsPackaged(appId) && appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? appId[..^4]
            : null;
    }

    /// <summary>The name to show until a better one is found: the program's name, or else the id.</summary>
    public static string FallbackName(string appId)
    {
        return ProgramName(appId) ?? appId;
    }

    /// <summary>
    /// The description of the program at <paramref name="path"/>, as Task Manager shows it. Null when it has none or
    /// the file is gone.
    /// </summary>
    public static string? ProgramDescription(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() is { Length: > 0 } description
                ? description
                : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
