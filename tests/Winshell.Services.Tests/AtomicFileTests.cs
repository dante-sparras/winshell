#pragma warning disable CA1416
using System.Runtime.InteropServices;

namespace Winshell.Services.Tests;

/// <summary>A save follows a symbolic link, writes through a hard link, and loses to a hand edit that lands during the replace.</summary>
public sealed class AtomicFileTests
{
    [Fact]
    public void A_hand_save_wins_only_when_the_disk_matches_neither_side_of_the_write()
    {
        Assert.False(AtomicFile.HandSaveWins(null, "a", "b"));
        Assert.False(AtomicFile.HandSaveWins("a", "a", "b"));
        Assert.False(AtomicFile.HandSaveWins("b", "a", "b"));
        Assert.True(AtomicFile.HandSaveWins("c", "a", "b"));
        Assert.True(AtomicFile.HandSaveWins("c", null, "b"));
    }

    [Fact]
    public void A_hand_save_that_lands_before_the_replace_is_not_overwritten()
    {
        var dir = Directory.CreateTempSubdirectory("winshell-atomic-");
        var path = Path.Combine(dir.FullName, "settings.toml");
        const string Hand = "name = \"by hand\"\n";
        File.WriteAllText(path, "name = \"file\"\n");
        var diskAtStart = File.ReadAllText(path);
        const string Incoming = "name = \"file\"\non = false\n";
        try
        {
            var wrote = AtomicFile.Write(path, Incoming, bom: false, inPlace: false, () =>
            {
                File.WriteAllText(path, Hand);
                return AtomicFile.HandSaveWins(File.ReadAllText(path), diskAtStart, Incoming);
            });

            Assert.False(wrote);
            var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Equal(Hand, text);
            Assert.False(File.Exists(path + ".tmp-" + Environment.ProcessId));
            Assert.Empty(Directory.GetFiles(dir.FullName, "settings.toml.tmp-*"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

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
            AtomicFile.DeleteStaleTemps(path);

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

    [Fact]
    public void Each_save_through_a_hard_link_updates_both_names()
    {
        using var dir = Temp();
        var (real, link) = Pair(dir);
        File.WriteAllText(real, "old\n");
        HardLink(link, real);

        foreach (var text in new[] { "one\n", "two\n" })
        {
            Assert.True(Save(link, text));

            Assert.Contains(text, File.ReadAllText(real), StringComparison.Ordinal);
            Assert.Contains(text, File.ReadAllText(link), StringComparison.Ordinal);
            Assert.True(AtomicFile.SaveTarget(link, out var hardLink).Length > 0);
            Assert.True(hardLink);
        }
    }

    [Fact]
    public void Each_save_through_a_symbolic_link_updates_the_target_and_keeps_the_link()
    {
        using var dir = Temp();
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
        var target = AtomicFile.SaveTarget(path, out var hardLink);
        return AtomicFile.Write(target, text, bom: false, inPlace: hardLink, () => false);
    }

    private static TempDirectory Temp() => new(Directory.CreateTempSubdirectory("winshell-atomic-"));

    private static (string Real, string Link) Pair(TempDirectory dir) =>
        (Path.Combine(dir.Path, "real.toml"), Path.Combine(dir.Path, "link.toml"));

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
        if (!CreateHardLinkW(link, real, IntPtr.Zero))
            throw new InvalidOperationException("CreateHardLinkW failed: " + Marshal.GetLastWin32Error());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    private sealed class TempDirectory(DirectoryInfo directory) : IDisposable
    {
        public string Path { get; } = directory.FullName;

        public void Dispose() => directory.Delete(true);
    }
}
