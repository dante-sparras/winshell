using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using SkiaSharp;
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

    [Fact]
    public async Task Art_over_the_size_limit_is_dropped_without_a_file()
    {
        var art = new byte[ArtCache.MaxBytes + 1];
        Png(2, 2).CopyTo(art, 0);

        Assert.Null(await ArtCache.StoreAsync(new MemoryStream(art), "image/png"));
        Assert.False(Stored(art));
    }

    [Fact]
    public async Task Svg_art_is_dropped_without_a_file()
    {
        var art = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100000\" height=\"100000\"/>");

        Assert.Null(await ArtCache.StoreAsync(new MemoryStream(art), "image/png"));
        Assert.Null(await ArtCache.StoreAsync(new MemoryStream(art), "image/svg+xml"));
        Assert.False(Stored(art));
    }

    [Fact]
    public async Task Art_that_decodes_to_too_many_pixels_is_dropped_without_a_file()
    {
        var art = Png(8193, 2049);

        Assert.Null(await ArtCache.StoreAsync(new MemoryStream(art), "image/png"));
        Assert.False(Stored(art));
    }

    [Fact]
    public async Task A_png_within_the_limits_is_stored()
    {
        var art = Png(3, 2);

        var url = await ArtCache.StoreAsync(new MemoryStream(art), "image/png");

        Assert.False(string.IsNullOrEmpty(url));
        Assert.True(Stored(art));
        ArtCache.Release(url);
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.Black);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static bool Stored(byte[] art)
    {
        var folder = Path.Combine(Path.GetTempPath(), "winshell", "media-art", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var name = Convert.ToHexString(SHA256.HashData(art))[..32].ToLowerInvariant();
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, name + ".*").Any();
    }
}
