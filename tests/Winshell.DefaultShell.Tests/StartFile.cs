using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

/// <summary>What Start wrote to <c>launcher.toml</c>, read back as the next run reads it.</summary>
internal static class StartFile
{
    public static string[] Pins(string path)
    {
        using var file = new StartPins(path, new ManualEventLoop());
        return [.. file.Pins!.Cast<string>()];
    }
}
