using System.Buffers.Binary;
using System.Globalization;
using Winshell.Media;

namespace Winshell.Services.Tests;

public class ArtCacheTests
{
    [Fact]
    public async Task WebP_bytes_are_stored_with_a_webp_extension()
    {
        var art = Convert.FromBase64String(
            "UklGRjAAAABXRUJQVlA4ICQAAADQAQCdASoDAAIAAUAmJZwCdAEfbfUlgAD+/wuX85lhocmFMAA=");
        var url = await ArtCache.StoreAsync(new MemoryStream(art));
        try
        {
            Assert.NotNull(url);
            Assert.EndsWith(".webp", new Uri(url).LocalPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (url is { Length: > 0 })
                ArtCache.Release(url);
        }
    }

    [Fact]
    public async Task Art_is_kept_in_a_folder_of_this_process_and_deleted_after_its_last_use()
    {
        var art = Png(2, 2).Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var url = await ArtCache.StoreAsync(new MemoryStream(art));
        Assert.NotNull(url);
        var path = new Uri(url).LocalPath;
        // Another instance deleting its copy of the same art must not take this one's.
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(url, await ArtCache.StoreAsync(new MemoryStream(art)));

        ArtCache.Release(url);
        Assert.True(File.Exists(path));
        ArtCache.Release(url);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task No_art_is_no_url()
    {
        Assert.Equal("", await ArtCache.StoreAsync(new MemoryStream()));
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
}
