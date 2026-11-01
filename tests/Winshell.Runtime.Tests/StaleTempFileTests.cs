using System.Diagnostics;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A crashed save leaves <c>file.tmp-&lt;pid&gt;</c>. The next save deletes one whose process is gone.</summary>
public class StaleTempFileTests
{
    [Fact]
    public void A_temp_from_a_dead_process_is_removed_and_a_live_one_stays()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", "temps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "preferences.toml");
                var dead = path + ".tmp-" + DeadPid;
        var deadGuid = path + ".tmp-" + DeadPid + "-" + Guid.NewGuid().ToString("N");
        var live = path + ".tmp-" + Environment.ProcessId;
        var liveGuid = path + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
        var other = Path.Combine(dir, "other.toml.tmp-" + DeadPid);
        var junk = path + ".tmp-not-a-pid";
        File.WriteAllText(dead, "dead");
        File.WriteAllText(deadGuid, "dead-guid");
        File.WriteAllText(live, "live");
        File.WriteAllText(liveGuid, "live-guid");
        File.WriteAllText(other, "other");
        File.WriteAllText(junk, "junk");
        try
        {
            FileView.DeleteStaleTemps(path);

            Assert.False(File.Exists(dead));
            Assert.False(File.Exists(deadGuid));
            Assert.False(File.Exists(junk));
            Assert.True(File.Exists(live));
            Assert.True(File.Exists(liveGuid));
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Process ids are multiples of four, so this one is never in use.
    private const int DeadPid = 999_999;
}
