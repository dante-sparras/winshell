using Winshell.TestSupport;

namespace Winshell.Runtime.Tests;

public class TempFolderCleanupTests
{
    [Fact]
    public void A_folder_older_than_a_day_is_removed_and_a_new_one_stays()
    {
        var root = Path.Combine(Path.GetTempPath(), "winshell-tests");
        Directory.CreateDirectory(root);
        var stale = Path.Combine(root, "stale-" + Guid.NewGuid().ToString("N"));
        var fresh = Path.Combine(root, "fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(fresh);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        try
        {
            TempFolderCleanup.Prune(TimeSpan.FromDays(1));

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(fresh));
        }
        finally
        {
            if (Directory.Exists(fresh))
                Directory.Delete(fresh, true);
        }
    }
}
