using System.Runtime.CompilerServices;

namespace Winshell.TestSupport;

/// <summary>
/// A new folder under <c>%TEMP%\winshell-tests</c>, deleted on dispose. Every test program prunes folders a crashed
/// run left there once at startup; a folder from today stays.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winshell-tests");

    public TempFolder()
    {
        Path = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, true);
    }

    [ModuleInitializer]
    internal static void PruneAtStart()
    {
        if (!Directory.Exists(Root))
            return;
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var dir in Directory.EnumerateDirectories(Root))
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) >= cutoff)
                    continue;
                Directory.Delete(dir, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
    }
}
