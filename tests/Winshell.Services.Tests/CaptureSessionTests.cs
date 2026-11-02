using System.Runtime.InteropServices;
using Winshell.Capture;

namespace Winshell.Services.Tests;

/// <summary>Capture over a fake backend and a fake clipboard: no frame is read from Windows, and the clipboard is left alone.</summary>
public sealed class CaptureSessionTests
{
    private static readonly CaptureTarget Screen = new(0, "display");

    private static PixelImage Red(int width = 4, int height = 2)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
            (bgra[i + 2], bgra[i + 3]) = (255, 255);
        return new PixelImage(width, height, bgra);
    }

    [Fact]
    public void Start_is_explicit_and_stop_clears_pixels_and_cancels_reads()
    {
        var loop = new ManualEventLoop();
        var backend = new FakeCapture();
        using var session = new CaptureSession(loop, () => backend) { Target = Screen, IncludeCursor = true };
        Assert.False(session.Running);
        Assert.Equal(0, backend.Starts);

        Assert.True(session.Start());
        Assert.True(session.Running);
        Assert.True(backend.Cursor);
        Assert.Equal(Screen, backend.Target);
        var ready = 0;
        session.FrameReady += () => ready++;
        var frame = Red();
        backend.Next = frame;
        loop.Advance(TimeSpan.FromMilliseconds(33));

        Assert.Same(frame, session.Frame);
        Assert.True(session.HasFrame);
        Assert.Equal(new QSize(4, 2), session.PixelSize);
        Assert.Equal(1, ready);

        session.Stop();

        Assert.False(session.Running);
        Assert.False(session.HasFrame);
        Assert.Null(session.Frame);
        Assert.Equal(new QSize(0, 0), session.PixelSize);
        Assert.Equal(1, backend.Disposals);
        loop.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, backend.Reads);
    }

    [Fact]
    public void No_new_frame_keeps_the_last_one_and_raises_nothing()
    {
        var loop = new ManualEventLoop();
        var backend = new FakeCapture { Next = Red() };
        using var session = new CaptureSession(loop, () => backend) { Target = Screen };
        var ready = 0;
        session.FrameReady += () => ready++;
        Assert.True(session.Start());

        loop.Advance(TimeSpan.FromMilliseconds(66));

        Assert.True(session.HasFrame);
        Assert.Equal(1, ready);
        Assert.Equal(2, backend.Reads);
    }

    [Fact]
    public void Changing_the_target_stops_and_a_stale_close_cannot_stop_the_next_capture()
    {
        var loop = new ManualEventLoop();
        var backends = new List<FakeCapture>();
        using var session = new CaptureSession(loop, () =>
        {
            var backend = new FakeCapture();
            backends.Add(backend);
            return backend;
        }) { Target = Screen };
        Assert.True(session.Start());
        var stale = backends[0].Closed!;

        session.Target = CaptureTarget.ForWindow(123);
        Assert.False(session.Running);
        Assert.Equal(1, backends[0].Disposals);

        Assert.True(session.Start());
        Assert.Equal(new CaptureTarget(123, ""), backends[1].Target);
        stale();
        loop.Process();
        Assert.True(session.Running);
    }

    [Fact]
    public void A_source_that_closes_stops_on_the_loop()
    {
        var loop = new ManualEventLoop();
        var backend = new FakeCapture();
        using var session = new CaptureSession(loop, () => backend) { Target = Screen };
        Assert.True(session.Start());

        backend.Closed!();
        Assert.True(session.Running);
        loop.Process();

        Assert.False(session.Running);
        Assert.Equal(1, backend.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Denial_and_device_errors_release_resources_and_report_why(bool duringRead)
    {
        var loop = new ManualEventLoop();
        var backend = new FakeCapture { FailStart = !duringRead, FailRead = duringRead };
        using var session = new CaptureSession(loop, () => backend) { Target = Screen };

        Assert.Equal(duringRead, session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));

        Assert.False(session.Running);
        Assert.NotEmpty(session.Error);
        Assert.Equal(1, backend.Disposals);
    }

    [Fact]
    public void A_session_with_no_target_does_not_make_a_backend()
    {
        using var session = new CaptureSession(new ManualEventLoop(), () => throw new InvalidOperationException("Must not create"));

        Assert.False(session.Start());

        Assert.Contains("Choose", session.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_teardown_error_still_clears_pixels()
    {
        var loop = new ManualEventLoop();
        var backend = new FakeCapture { Next = Red(), FailDispose = true };
        var session = new CaptureSession(loop, () => backend) { Target = Screen };
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));

        session.Dispose();

        Assert.False(session.Running);
        Assert.False(session.HasFrame);
        Assert.NotEmpty(session.Error);
        loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, backend.Reads);
        Assert.False(session.Start());
    }

    [Fact]
    public void The_interval_is_bounded_and_a_frame_handler_can_stop_capture()
    {
        var loop = new ManualEventLoop();
        var backend = new FakeCapture { Next = Red() };
        using var session = new CaptureSession(loop, () => backend) { Target = Screen, FrameInterval = 0 };
        Assert.Equal(16, session.FrameInterval);
        session.FrameInterval = int.MaxValue;
        Assert.Equal(1000, session.FrameInterval);
        session.FrameInterval = 16;
        session.FrameReady += session.Stop;

        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromSeconds(5));

        Assert.False(session.Running);
        Assert.Equal(1, backend.Reads);
    }

    [Fact]
    public void A_saved_png_never_overwrites_a_file_and_never_goes_to_a_remote_url()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.png");
        var loop = new ManualEventLoop();
        var backend = new FakeCapture { Next = Red(8, 6) };
        using var session = new CaptureSession(loop, () => backend) { Target = Screen };
        try
        {
            Assert.False(session.SaveToFile(path));
            Assert.True(session.Start());
            loop.Advance(TimeSpan.FromMilliseconds(33));

            Assert.True(session.SaveToFile(path));
            Assert.Empty(session.Error);
            var saved = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, saved[..4]);

            File.WriteAllText(path, "kept");
            Assert.False(session.SaveToFile(path));
            Assert.NotEmpty(session.Error);
            Assert.Equal("kept", File.ReadAllText(path));

            Assert.False(session.SaveToFile("https://example.com/test.png"));
            Assert.False(session.SaveToFile(""));
            Assert.False(session.SaveToFile(Path.Combine(directory, "missing", "test.png")));
            Assert.True(session.SaveToFile(new Uri(Path.Combine(directory, "url.png")).AbsoluteUri));
            Assert.True(File.Exists(Path.Combine(directory, "url.png")));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // The indexer can still have the new file open after this process has closed it.
            }
        }
    }

    [Fact]
    public void Copying_needs_a_frame_and_a_window_and_reports_a_failure()
    {
        var loop = new ManualEventLoop();
        var frame = Red();
        var backend = new FakeCapture { Next = frame };
        var copied = new List<(PixelImage Image, nint Owner)>();
        Exception? refuse = null;
        using var session = new CaptureSession(loop, () => backend, (image, owner) =>
        {
            if (refuse is not null)
                throw refuse;
            copied.Add((image, owner));
        }) { Target = Screen };

        Assert.False(session.CopyToClipboard(7));
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));
        Assert.False(session.CopyToClipboard(0));
        Assert.Empty(copied);

        Assert.True(session.CopyToClipboard(7));
        Assert.Empty(session.Error);
        Assert.Equal((frame, (nint)7), Assert.Single(copied));

        refuse = new InvalidOperationException("Clipboard busy");
        Assert.False(session.CopyToClipboard(7));
        Assert.Equal("Clipboard busy", session.Error);
    }

    [Fact]
    public void The_clipboard_gets_a_top_down_header_and_straight_alpha()
    {
        var dib = CaptureClipboard.EncodeDib(new PixelImage(2, 1, [64, 32, 16, 128, 0, 0, 255, 255], premultiplied: true));

        Assert.Equal(132, dib.Length);
        Assert.Equal(124, BitConverter.ToInt32(dib, 0));
        Assert.Equal(2, BitConverter.ToInt32(dib, 4));
        Assert.Equal(-1, BitConverter.ToInt32(dib, 8));
        Assert.Equal(32, BitConverter.ToInt16(dib, 14));
        Assert.Equal(3, BitConverter.ToInt32(dib, 16));
        Assert.Equal(0x00ff0000u, BitConverter.ToUInt32(dib, 40));
        Assert.Equal(0xff000000u, BitConverter.ToUInt32(dib, 52));
        Assert.Equal(new byte[] { 128, 64, 32, 128, 0, 0, 255, 255 }, dib[124..]);
    }

    [Theory]
    [InlineData("C:\\shots\\a.png", "C:\\shots\\a.png")]
    [InlineData("a.png", "a.png")]
    [InlineData("file:///C:/shots/a.png", "C:\\shots\\a.png")]
    [InlineData("https://example.com/a.png", null)]
    [InlineData("data:image/png;base64,AAAA", null)]
    [InlineData(" ", null)]
    public void Only_a_local_path_is_a_place_to_save(string path, string? local) =>
        Assert.Equal(local, CaptureSession.LocalPath(path));

    [Fact]
    public void Readback_respects_both_row_pitches()
    {
        var source = new byte[] { 1, 2, 3, 4, 99, 99, 99, 99, 5, 6, 7, 8, 99, 99, 99, 99 };
        var destination = new byte[12];
        var from = GCHandle.Alloc(source, GCHandleType.Pinned);
        var to = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try
        {
            WindowsCaptureBackend.CopyRows(from.AddrOfPinnedObject(), 8, to.AddrOfPinnedObject(), 6, 1, 2);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0, 5, 6, 7, 8, 0, 0 }, destination);
        }
        finally
        {
            from.Free();
            to.Free();
        }
    }

    private sealed class FakeCapture : ICaptureBackend
    {
        public CaptureTarget? Target { get; private set; }

        public Action? Closed { get; private set; }

        public bool Cursor { get; private set; }

        public int Starts { get; private set; }

        public int Reads { get; private set; }

        public int Disposals { get; private set; }

        public PixelImage? Next { get; set; }

        public bool FailStart { get; init; }

        public bool FailRead { get; init; }

        public bool FailDispose { get; init; }

        public void Start(CaptureTarget target, bool includeCursor, Action closed)
        {
            Starts++;
            Target = target;
            Cursor = includeCursor;
            Closed = closed;
            if (FailStart)
                throw new UnauthorizedAccessException("Capture denied by Windows policy.");
        }

        public PixelImage? ReadLatest()
        {
            Reads++;
            if (FailRead)
                throw new COMException("Device removed.");
            var next = Next;
            Next = null;
            return next;
        }

        public void Dispose()
        {
            Disposals++;
            if (FailDispose)
                throw new COMException("Device removed during teardown.");
        }
    }
}
