using System.Globalization;
using System.Diagnostics;
using Winshell.Platform.Windows.Services.Media;

namespace Winshell.Platform.Windows.Tests;

public class ArtCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cleanup_leaves_files_reached_through_a_directory_link_alone(bool linkedRoot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(directory, "cache");
        var elsewhere = Path.Combine(directory, "elsewhere");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(elsewhere);
        var outside = Path.Combine(elsewhere, new string('a', 32) + ".png");
        File.WriteAllText(outside, "not the cache's to delete");
        File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-3));
        var link = linkedRoot ? Path.Combine(directory, "linked-cache") : Path.Combine(cache, "1234");
        try
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, elsewhere })
                start.ArgumentList.Add(argument);
            using var command = Process.Start(start)!;
            command.WaitForExit();
            Assert.Equal(0, command.ExitCode);
            Assert.NotNull(new DirectoryInfo(link).LinkTarget);

            ArtCache.CleanOld(linkedRoot ? link : cache);

            Assert.True(File.Exists(outside), "Cleanup followed a junction and removed a file outside the cache.");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Cleanup_removes_old_art_but_keeps_recent_art()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        var processFolder = Path.Combine(directory, "1234");
        Directory.CreateDirectory(processFolder);
        var old = Path.Combine(processFolder, new string('a', 32) + ".png");
        var recent = Path.Combine(processFolder, new string('b', 32) + ".png");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
        File.WriteAllText(recent, "recent");
        try
        {
            ArtCache.CleanOld(directory);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Art_is_kept_in_a_folder_of_this_process_and_deleted_after_its_last_use()
    {
        var art = Guid.NewGuid().ToByteArray();
        var url = ArtCache.Store(art, "image/png");
        var path = new Uri(url).LocalPath;
        // Another instance deleting its copy of the same art must not take this one's.
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(url, ArtCache.Store(art, "image/png"));

        ArtCache.Release(url);
        Assert.True(File.Exists(path));
        ArtCache.Release(url);
        Assert.False(File.Exists(path));
    }
}
