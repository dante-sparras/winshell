using System.Buffers.Binary;
using System.Text;

namespace Winshell.Services.Tests;

public sealed class TempImageStoreTests
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
            ChildProcess.Run("cmd.exe", "/c", "mklink", "/J", link, elsewhere).AssertExitCode(0);
            Assert.NotNull(new DirectoryInfo(link).LinkTarget);

            new TempImageStore(linkedRoot ? link : cache).CleanOld();

            Assert.True(File.Exists(outside), "Cleanup followed a junction and removed a file outside the cache.");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Cleanup_removes_old_pictures_but_keeps_recent_ones()
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
            new TempImageStore(directory).CleanOld();
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_picture_at_the_pixel_cap_is_stored_and_one_pixel_over_is_not()
    {
        using var directory = Temp();
        var store = new TempImageStore(directory.Path);
        var kept = store.Store("cap", Png(4096, 4096));
        Assert.NotNull(kept);
        Assert.EndsWith(".png", new Uri(kept).LocalPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(new Uri(kept).LocalPath));

        Assert.Null(store.Store("over", Png(4097, 4096)));
        store.Release("cap");
        Assert.False(File.Exists(new Uri(kept).LocalPath));
    }

    [Fact]
    public void A_second_use_keeps_the_picture_until_both_are_released()
    {
        using var directory = Temp();
        var store = new TempImageStore(directory.Path);
        var url = store.Store("logo", Png(2, 2));
        Assert.NotNull(url);
        Assert.Equal(url, store.Retain("logo"));

        var path = new Uri(url).LocalPath;
        store.Release("logo");
        Assert.True(File.Exists(path));
        store.Release("logo");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_file_still_open_at_the_last_release_is_reused()
    {
        using var directory = Temp();
        var store = new TempImageStore(directory.Path);
        var png = Png(2, 2);
        var url = store.Store("same", png);
        Assert.NotNull(url);
        var path = new Uri(url).LocalPath;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Release("same");
            Assert.Equal(url, store.Store("same", png));
            Assert.Equal(png, File.ReadAllBytes(path));
        }

        store.Release("same");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_picture_with_no_pixels_is_not_stored()
    {
        using var directory = Temp();
        Assert.Null(new TempImageStore(directory.Path).Store("none", Png(0, 2)));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_picture_over_the_byte_cap_is_not_stored()
    {
        var art = new byte[TempImageStore.MaxBytes + 1];
        Png(2, 2).CopyTo(art, 0);
        using var directory = Temp();
        var store = new TempImageStore(directory.Path);

        Assert.Null(store.Read(new MemoryStream(art), TestContext.Current.CancellationToken));
        Assert.Null(await store.ReadAsync(new MemoryStream(art), TestContext.Current.CancellationToken));
        Assert.Null(store.Store("too-big", art));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Svg_is_not_stored()
    {
        var svg = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100000\" height=\"100000\"/>");
        using var directory = Temp();
        var store = new TempImageStore(directory.Path);

        Assert.Null(store.Store("svg", svg));
        Assert.False(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories).Any());
    }

    private static TempDirectory Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    private static byte[] Png(int width, int height)
    {
        var data = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(data, 0);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)height);
        return data;
    }

    private sealed class TempDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
