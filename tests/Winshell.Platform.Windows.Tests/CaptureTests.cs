using System.Runtime.InteropServices;
using SkiaSharp;
using Winshell.Platform.Windows.Services.Capture;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

public class CaptureTests
{
    private static ShellScreen Screen() => new(new ScreenInfo("display", "display", "", "", "", new QRect(1920, 0, 800, 600),
        new QRect(1920, 0, 800, 600), 2, 0, 0, 60, false));

    private static SKImage Pixels(int width = 4, int height = 2)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Red);
        return surface.Snapshot();
    }

    [Fact]
    public void Start_is_explicit_and_stop_clears_pixels_and_cancels_reads()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture();
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen(), IncludeCursor = true };
        Assert.False(session.Running);
        Assert.Equal(0, backend.Starts);
        Assert.True(session.Start());
        Assert.True(backend.Cursor);
        Assert.Equal(new CaptureTarget(0, "display"), backend.Target);
        var ready = 0;
        session.Connect("frameReady", _ => ready++);
        backend.Next = Pixels();
        loop.Advance(TimeSpan.FromMilliseconds(33));
        Assert.True(session.HasFrame);
        Assert.Equal(new QSize(4, 2), session.PixelSize);
        Assert.Equal(1, ready);
        session.Stop();
        Assert.False(session.Running);
        Assert.False(session.HasFrame);
        Assert.Equal(new QSize(0, 0), session.PixelSize);
        Assert.Equal(1, backend.Disposals);
        loop.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, backend.Reads);
        session.Stop();
        session.Destroy();
        Assert.Equal(1, backend.Disposals);
    }

    [Fact]
    public void No_frame_does_not_emit_and_new_frames_replace_the_previous_image()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture();
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        var ready = 0;
        session.Connect("frameReady", _ => ready++);
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));
        Assert.False(session.HasFrame);
        Assert.Equal(0, ready);
        backend.Next = Pixels();
        loop.Advance(TimeSpan.FromMilliseconds(33));
        var previous = session.Image!;
        backend.Next = Pixels(8, 6);
        loop.Advance(TimeSpan.FromMilliseconds(33));
        Assert.Equal(nint.Zero, previous.Handle);
        Assert.Equal(new QSize(8, 6), session.PixelSize);
        Assert.Equal(2, ready);
        session.Destroy();
    }

    [Fact]
    public void Stale_close_callbacks_cannot_stop_a_new_session_and_target_destruction_stops_it()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backends = new List<FakeCapture>();
        var target = Screen();
        var session = new CaptureSession(() => { var b = new FakeCapture(); backends.Add(b); return b; }) { Engine = engine, Target = target };
        Assert.True(session.Start());
        var stale = backends[0].Closed!;
        session.Target = Screen();
        Assert.False(session.Running);
        Assert.Equal(1, backends[0].Disposals);
        Assert.True(session.Start());
        stale();
        target.Destroy(); // An old target must not stop the replacement.
        loop.Process();
        Assert.True(session.Running);
        session.Target!.Destroy();
        Assert.False(session.Running);
        Assert.Null(session.Target);
        Assert.Equal(1, backends[1].Disposals);
        session.Destroy();
    }

    [Fact]
    public void Source_close_is_marshaled_to_the_loop_and_releases_the_session()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture();
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        Assert.True(session.Start());
        backend.Closed!();
        Assert.True(session.Running);
        loop.Process();
        Assert.False(session.Running);
        Assert.Equal(1, backend.Disposals);
        session.Destroy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Denial_and_device_errors_release_resources_and_report_error(bool duringRead)
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture { FailStart = !duringRead, FailRead = duringRead };
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        Assert.Equal(duringRead, session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));
        Assert.False(session.Running);
        Assert.NotEmpty(session.Error);
        Assert.Equal(1, backend.Disposals);
        session.Destroy();
    }

    [Fact]
    public void Invalid_targets_and_missing_loop_fail_without_constructing_a_backend()
    {
        var session = new CaptureSession(() => throw new Exception("Must not create"));
        Assert.False(session.Start());
        Assert.Contains("Choose", session.Error, StringComparison.Ordinal);
        session.Target = new QObject();
        Assert.False(session.Start());
        session.Target = Screen();
        Assert.False(session.Start());
        Assert.Contains("event loop", session.Error, StringComparison.Ordinal);
        session.Destroy();
        Assert.False(session.Start());
    }

    [Fact]
    public void A_native_teardown_error_still_clears_pixels_and_finishes_destruction()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture { Next = Pixels(), FailDispose = true };
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));
        var image = session.Image!;
        session.Destroy();
        Assert.True(session.IsDestroyed);
        Assert.False(session.Running);
        Assert.False(session.HasFrame);
        Assert.Equal(nint.Zero, image.Handle);
        Assert.NotEmpty(session.Error);
        loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, backend.Reads);
    }

    [Fact]
    public void Interval_is_bounded_and_a_frame_handler_can_stop_capture()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture { Next = Pixels() };
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen(), FrameInterval = 0 };
        Assert.Equal(16, session.FrameInterval);
        session.FrameInterval = int.MaxValue;
        Assert.Equal(1000, session.FrameInterval);
        session.FrameInterval = 16;
        session.Connect("frameReady", _ => session.Stop());
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromSeconds(5));
        Assert.False(session.Running);
        Assert.Equal(1, backend.Reads);
        session.Destroy();
    }

    [Fact]
    public void Png_export_uses_physical_pixels_and_never_overwrites_or_writes_remote_urls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop, shellDir: directory);
        var backend = new FakeCapture { Next = Pixels(8, 6) };
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        try
        {
            Assert.False(session.SaveToFile("test.png"));
            Assert.True(session.Start());
            loop.Advance(TimeSpan.FromMilliseconds(33));
            Assert.True(session.SaveToFile("test.png"));
            // Decode a copy. Opening the path keeps it mapped until a finalizer, and deleting it then fails.
            using var png = SKBitmap.Decode(File.ReadAllBytes(Path.Combine(directory, "test.png")));
            Assert.Equal(8, png.Width);
            Assert.Equal(6, png.Height);
            Assert.Equal(SKColors.Red, png.GetPixel(0, 0));
            Assert.False(session.SaveToFile("test.png"));
            Assert.False(session.SaveToFile("https://example.com/test.png"));
            Assert.False(session.SaveToFile(""));
            Assert.False(session.CopyToClipboard(null));
        }
        finally
        {
            session.Destroy();
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
    public void Preview_fits_the_frame_and_clears_when_session_is_destroyed()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture { Next = Pixels(4, 2) };
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        var view = new CaptureView { Session = session, Width = 8, Height = 8 };
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));
        Assert.Equal(4, view.ImplicitWidth);
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        surface.Canvas.Clear(SKColors.Transparent);
        view.Paint(surface.Canvas, new PaintContext());
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(4, 4));
        Assert.Equal(0, bitmap.GetPixel(4, 0).Alpha);
        session.Destroy();
        Assert.Null(view.Session);
        Assert.Equal(0, view.ImplicitWidth);
        view.Destroy();
    }

    [Fact]
    public void Clipboard_encoding_has_top_down_straight_alpha_Bgra_and_explicit_masks()
    {
        using var image = Pixels(2, 1);
        var dib = CaptureClipboard.EncodeDib(image);
        Assert.Equal(132, dib.Length);
        Assert.Equal(124, BitConverter.ToInt32(dib, 0));
        Assert.Equal(2, BitConverter.ToInt32(dib, 4));
        Assert.Equal(-1, BitConverter.ToInt32(dib, 8));
        Assert.Equal(32, BitConverter.ToInt16(dib, 14));
        Assert.Equal(3, BitConverter.ToInt32(dib, 16));
        Assert.Equal(0x00ff0000u, BitConverter.ToUInt32(dib, 40));
        Assert.Equal(0xff000000u, BitConverter.ToUInt32(dib, 52));
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, dib[124..]);
    }

    [Fact]
    public void Preview_implicit_size_uses_its_host_DPI_while_exports_remain_physical()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var backend = new FakeCapture { Next = Pixels(8, 6) };
        var session = new CaptureSession(() => backend) { Engine = engine, Target = Screen() };
        var view = new CaptureView { Session = session };
        Assert.True(session.Start());
        loop.Advance(TimeSpan.FromMilliseconds(33));
        view.SetHost(new PreviewHost(2));
        Assert.Equal(4, view.ImplicitWidth);
        Assert.Equal(3, view.ImplicitHeight);
        Assert.Equal(new QSize(8, 6), session.PixelSize);
        view.SetHost(new PreviewHost(1.5));
        Assert.Equal(8 / 1.5, view.ImplicitWidth);
        view.Destroy();
        session.Destroy();
    }

    [Fact]
    public void Disposing_the_engine_releases_active_capture_and_rejects_queued_callbacks()
    {
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        var path = Path.Combine(Path.GetTempPath(), "capture-owned.qml");
        engine.LoadDocumentFromText("import QtQuick\nItem {}", path);
        var root = engine.CreateRoot(path);
        var backend = new FakeCapture { Next = Pixels() };
        var target = new AppWindow(null!, 123);
        var session = new CaptureSession(() => backend) { Engine = engine, Target = target };
        session.SetOwner(root);
        Assert.True(session.Start());
        Assert.Equal(new CaptureTarget(123, ""), backend.Target);
        loop.Advance(TimeSpan.FromMilliseconds(33));
        var image = session.Image!;
        backend.Closed!();
        engine.Dispose();
        loop.Advance(TimeSpan.FromSeconds(10));
        Assert.True(session.IsDestroyed);
        Assert.Equal(nint.Zero, image.Handle);
        Assert.Equal(1, backend.Disposals);
        Assert.Equal(1, backend.Reads);
        target.Destroy();
    }

    [Fact]
    public void Readback_respects_both_row_pitches_without_copying_GPU_padding()
    {
        var source = new byte[] { 1, 2, 3, 4, 99, 99, 99, 99, 5, 6, 7, 8, 99, 99, 99, 99 };
        var destination = new byte[12];
        var from = GCHandle.Alloc(source, GCHandleType.Pinned);
        var to = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try
        {
            WindowsCaptureBackend.CopyRows(from.AddrOfPinnedObject(), 8, to.AddrOfPinnedObject(), 6, 1, 2);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0, 5, 6, 7, 8, 0, 0 }, destination);
            Assert.Throws<ArgumentOutOfRangeException>(() => WindowsCaptureBackend.CopyRows(from.AddrOfPinnedObject(), 2, to.AddrOfPinnedObject(), 6, 1, 2));
        }
        finally { from.Free(); to.Free(); }
    }

    [Fact]
    public void Qml_module_loads_and_engine_destruction_releases_capture()
    {
        var modules = StandardModules.Create();
        CaptureModule.Register(modules);
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(modules, loop);
        engine.LoadDocumentFromText("""
            import QtQuick
            import Winshell.Capture
            Item {
              CaptureSession { id: capture; objectName: "capture" }
              CaptureView { objectName: "preview"; session: capture }
            }
            """, Path.Combine(Path.GetTempPath(), "capture-shell.qml"));
        var root = engine.CreateRoot(Path.Combine(Path.GetTempPath(), "capture-shell.qml"));
        var capture = Assert.IsType<CaptureSession>(root.OwnedObjects.Single(o => o.ObjectName == "capture"));
        Assert.False(capture.Running);
        var preview = Assert.IsType<CaptureView>(root.OwnedObjects.Single(o => o.ObjectName == "preview"));
        Assert.Same(capture, preview.Session);
        root.Destroy();
        Assert.True(capture.IsDestroyed);
        Assert.True(preview.IsDestroyed);
    }

    private sealed class FakeCapture : ICaptureBackend
    {
        public CaptureTarget? Target { get; private set; }
        public Action? Closed { get; private set; }
        public bool Cursor { get; private set; }
        public int Starts { get; private set; }
        public int Reads { get; private set; }
        public int Disposals { get; private set; }
        public SKImage? Next { get; set; }
        public bool FailStart { get; init; }
        public bool FailRead { get; init; }
        public bool FailDispose { get; init; }

        public void Start(CaptureTarget target, bool includeCursor, Action closed)
        {
            Starts++;
            Target = target;
            Cursor = includeCursor;
            Closed = closed;
            if (FailStart) throw new UnauthorizedAccessException("Capture denied by Windows policy.");
        }

        public SKImage? ReadLatest()
        {
            Reads++;
            if (FailRead) throw new COMException("Device removed.");
            var next = Next;
            Next = null;
            return next;
        }

        public void Dispose()
        {
            Disposals++;
            Next?.Dispose();
            Next = null;
            if (FailDispose) throw new COMException("Device removed during teardown.");
        }
    }

    private sealed class PreviewHost(double ratio) : IItemHost
    {
        public double DevicePixelRatio => ratio;
        public QObject? WindowObject => null;
        public Item? FocusItem => null;
        public void RequestUpdate() { }
        public void SetFocusItem(Item? item) { }
    }
}
