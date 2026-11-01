using System.Net;
using System.Net.Sockets;
using SkiaSharp;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

[Collection(ThemeCollection.Name)]
public sealed class ImageRemoteTests : IDisposable
{
    private readonly HttpClient _original = ImageLoader.Http;

    public void Dispose() => ImageLoader.Http = _original;

    private static Image Load(string source, out TestShell shell) => (Image)TestShell.Run($$"""
        import QtQuick
        Image { source: "{{source}}" }
        """, out shell);

    private static void WaitForStatus(TestShell shell, Image image) =>
        Wait.Until(() => image.Status != (int)Image.StatusValue.Loading, "the image to load", shell.Loop.Process);

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond());
        }
    }

    /// <summary>A body that cannot say how long it is, and counts the bytes read from it.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            BytesRead += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void An_http_source_is_not_fetched_and_reports_an_error()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var image = Load($"http://127.0.0.1:{port}/a.png", out var shell);
            using (shell)
            {
                WaitForStatus(shell, image);

                Assert.Equal((int)Image.StatusValue.Error, image.Status);
                Assert.False(listener.Pending());
                Assert.Contains(shell.Messages, m => m.Contains("only https:// images are loaded", StringComparison.Ordinal));
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void An_https_image_loads()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage { Content = new ByteArrayContent(Png()) });
        ImageLoader.Http = new HttpClient(handler);
        var image = Load($"https://example.test/{Guid.NewGuid():N}.png", out var shell);
        using (shell)
        {
            WaitForStatus(shell, image);

            Assert.Equal((int)Image.StatusValue.Ready, image.Status);
            Assert.Equal(1, handler.Requests);
        }
    }

    [Fact]
    public void An_https_body_that_says_it_is_over_the_cap_is_dropped()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage { Content = new ByteArrayContent(new byte[ImageLoader.MaxRemoteBytes + 1]) });
        ImageLoader.Http = new HttpClient(handler);
        var image = Load($"https://example.test/{Guid.NewGuid():N}.png", out var shell);
        using (shell)
        {
            WaitForStatus(shell, image);

            Assert.Equal((int)Image.StatusValue.Error, image.Status);
            Assert.Contains(shell.Messages, m => m.Contains("Cannot open", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_https_body_is_dropped_once_it_passes_the_cap_even_when_it_never_says_how_long_it_is()
    {
        var body = new EndlessStream();
        var handler = new FakeHandler(() => new HttpResponseMessage { Content = new StreamContent(body) });
        ImageLoader.Http = new HttpClient(handler);
        var image = Load($"https://example.test/{Guid.NewGuid():N}.png", out var shell);
        using (shell)
        {
            WaitForStatus(shell, image);

            Assert.Equal((int)Image.StatusValue.Error, image.Status);
            Assert.InRange(body.BytesRead, ImageLoader.MaxRemoteBytes, ImageLoader.MaxRemoteBytes + 200_000);
        }
    }
}
