using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Storage.Streams;
using Winshell.Media;

namespace Winshell.Services.Tests;

/// <summary>A session's art comes from a thumbnail Windows gives, which an app can fail to hand over.</summary>
public sealed class SessionArtTests
{
    [Fact]
    public async Task A_failed_read_keeps_the_art_shown_and_is_logged_once_until_art_arrives_again()
    {
        var loop = new ManualEventLoop();
        var missed = new List<string>();
        var changes = 0;
        var art = new SessionArt(loop, missed.Add, () => changes++);
        try
        {
            art.Load(await ThumbnailAsync());
            Wait.Until(() => art.Url.Length > 0, "the art", loop.Process);
            var shown = art.Url;

            art.Load(new Unreadable());
            art.Load(new Unreadable());
            loop.Process();

            Assert.Equal(shown, art.Url);
            Assert.Equal(1, changes);
            Assert.Equal(["the app is gone"], missed);
            Assert.False(art.Loading);

            art.Load(await ThumbnailAsync());
            Wait.Until(() => art.Url != shown, "the next art", loop.Process);
            art.Load(new Unreadable());
            loop.Process();

            Assert.Equal(2, missed.Count);
        }
        finally
        {
            art.Release();
        }
    }

    /// <summary>A PNG header with bytes of its own after it, so each one is a new file in the cache.</summary>
    private static async Task<IRandomAccessStreamReference> ThumbnailAsync()
    {
        var data = new byte[40];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(data, 0);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), 2);
        Guid.NewGuid().ToByteArray().CopyTo(data, 24);
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        return RandomAccessStreamReference.CreateFromStream(stream);
    }

    private sealed class Unreadable : IRandomAccessStreamReference
    {
        public IAsyncOperation<IRandomAccessStreamWithContentType> OpenReadAsync() => throw new COMException("the app is gone");
    }
}
