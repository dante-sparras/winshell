namespace Winshell.Core.Tests;

/// <summary>A save follows a symbolic link, writes through a hard link, and loses to a hand edit that lands during the replace.</summary>
public sealed class AtomicFileTests
{
    [Fact]
    public void A_hand_save_wins_only_when_the_disk_matches_neither_side_of_the_write()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "settings.toml");
        const string Incoming = "name = \"file\"\non = false\n";
        const string Read = "name = \"file\"\n";
        const string Hand = "name = \"by hand\"\n";

        Assert.Equal(AtomicSave.Saved, AtomicFile.Save(path, Incoming, false, null));
        Assert.Equal(Incoming, Text(path));

        File.WriteAllText(path, Read);
        Assert.Equal(AtomicSave.Saved, AtomicFile.Save(path, Incoming, false, Text(path)));
        Assert.Equal(Incoming, Text(path));

        Assert.Equal(AtomicSave.Saved, AtomicFile.Save(path, Incoming, false, Read));
        Assert.Equal(Incoming, Text(path));

        File.WriteAllText(path, Hand);
        Assert.Equal(AtomicSave.HandSaveWon, AtomicFile.Save(path, Incoming, false, Read));
        Assert.Equal(Hand, Text(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "settings.toml.tmp-*"));

        Assert.Equal(AtomicSave.HandSaveWon, AtomicFile.Save(path, Incoming, false, null));
        Assert.Equal(Hand, Text(path));
    }

    [Fact]
    public void A_temp_from_a_dead_process_is_removed_and_a_live_one_stays()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", "temps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "preferences.toml");
        var dead = path + ".tmp-" + DeadPid;
        var deadGuid = path + ".tmp-" + DeadPid + "-" + Guid.NewGuid().ToString("N");
        var liveGuid = path + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");
        var other = Path.Combine(dir, "other.toml.tmp-" + DeadPid);
        var junk = path + ".tmp-not-a-pid";
        File.WriteAllText(dead, "dead");
        File.WriteAllText(deadGuid, "dead-guid");
        File.WriteAllText(liveGuid, "live-guid");
        File.WriteAllText(other, "other");
        File.WriteAllText(junk, "junk");
        try
        {
            Assert.Equal(AtomicSave.Saved, AtomicFile.Save(path, "kept\n", false, null));

            Assert.False(File.Exists(dead));
            Assert.False(File.Exists(deadGuid));
            Assert.False(File.Exists(junk));
            Assert.True(File.Exists(liveGuid));
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Each_save_through_a_hard_link_updates_both_names()
    {
        using var dir = new TempFolder();
        var (real, link) = Pair(dir);
        File.WriteAllText(real, "old\n");
        HardLink(link, real);

        foreach (var text in new[] { "one\n", "two\n" })
        {
            Assert.True(Save(link, text));

            Assert.Contains(text, File.ReadAllText(real), StringComparison.Ordinal);
            Assert.Contains(text, File.ReadAllText(link), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Each_save_through_a_symbolic_link_updates_the_target_and_keeps_the_link()
    {
        using var dir = new TempFolder();
        var (real, link) = Pair(dir);
        File.WriteAllText(real, "old\n");
        if (!TryLink(link, real))
            return;

        foreach (var text in new[] { "one\n", "two\n" })
        {
            Assert.True(Save(link, text));

            Assert.Contains(text, File.ReadAllText(real), StringComparison.Ordinal);
            Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Contains(text, File.ReadAllText(link), StringComparison.Ordinal);
        }
    }

    // Process ids are multiples of four, so this one is never in use.
    private const int DeadPid = 999_999;

    private static bool Save(string path, string text)
    {
        var expected = File.Exists(path) ? File.ReadAllText(path) : null;
        return AtomicFile.Save(path, text, false, expected) == AtomicSave.Saved;
    }

    private static string Text(string path)
    {
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static (string Real, string Link) Pair(TempFolder dir)
    {
        return (Path.Combine(dir.Path, "real.toml"), Path.Combine(dir.Path, "link.toml"));
    }

    private static bool TryLink(string link, string real)
    {
        try
        {
            File.CreateSymbolicLink(link, real);
            return true;
        }
        catch (IOException e) when ((e.HResult & 0xFFFF) == 1314)
        {
            // ERROR_PRIVILEGE_NOT_HELD. This process cannot create a symbolic link, so the assertion
            // runs only where that privilege is held. The hard-link test covers the in-place path.
            return false;
        }
    }

    private static void HardLink(string link, string real)
    {
        ChildProcess.Run("cmd.exe", "/c", "mklink", "/H", link, real).AssertExitCode(0);
    }
}
