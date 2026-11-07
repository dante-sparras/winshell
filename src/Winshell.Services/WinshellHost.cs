using Winshell.Native;

namespace Winshell;

/// <summary>
/// <c>winshell.exe</c> and <c>winshellw.exe</c>, one program built twice. The second has no console: Start runs it,
/// and it still starts a shell when given no command.
/// </summary>
internal static class WinshellHost
{
    private const string ConsoleName = "winshell.exe";

    private const string WindowlessName = "winshellw.exe";

    public static bool Is(string path)
    {
        return Named(path, ConsoleName) || Named(path, WindowlessName);
    }

    public static bool IsWindowless(string? path)
    {
        return path is not null && Named(path, WindowlessName);
    }

    /// <summary>The console host beside a <c>winshellw.exe</c>. Any other path is unchanged.</summary>
    public static string ConsoleOf(string path)
    {
        if (!IsWindowless(path))
            return path;
        var folder = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(folder) ? ConsoleName : Path.Combine(folder, ConsoleName);
    }

    /// <summary>The host that started this process, or null when none did. A configuration's program is not a host.</summary>
    public static string? Supervising()
    {
        return ProcessImage.Ancestor(Is);
    }

    private static bool Named(string path, string name)
    {
        return Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
