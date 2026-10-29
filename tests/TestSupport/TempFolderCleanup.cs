using System.Runtime.CompilerServices;

namespace Winshell.TestSupport;

/// <summary>
/// Deletes <c>%TEMP%\winshell-tests</c> folders left behind by a crashed run. A folder from today stays. Each test
/// project that makes those folders links this file, so every test run prunes them once at startup.
/// </summary>
internal static class TempFolderCleanup
{
    [ModuleInitializer]
    internal static void PruneAtStart() => Prune(TimeSpan.FromDays(1));

    internal static void Prune(TimeSpan olderThan)
    {
        var root = Path.Combine(Path.GetTempPath(), "winshell-tests");
        if (!Directory.Exists(root))
            return;
        var cutoff = DateTime.UtcNow - olderThan;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
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
}
