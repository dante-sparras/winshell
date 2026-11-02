using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Winshell.Capture;

namespace Winshell.ShellTests;

/// <summary>The view shows a session's frames and lets go of it. What the screen then looks like is not asserted here.</summary>
public sealed class CaptureViewTests
{
    [Fact]
    public async Task A_capture_view_shows_the_frame_and_lets_go_when_it_leaves()
    {
        var kept = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var backend = new FakeCapture();
            using var session = new CaptureSession(shell.Loop, () => backend)
            {
                FrameInterval = 16,
                Target = CaptureTarget.ForWindow(1),
            };
            var view = new CaptureView
            {
                Width = 80,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Session = session,
            };
            using var popup = new PopupWindow
            {
                Rect = new QRect(screen.X + 40, screen.Y + 40, 200, 120),
                Content = new Grid { Children = { view } },
            };
            popup.Visible = true;
            await ShellFixture.LaidOut(view);

            var red = Solid(255, 0, 0);
            backend.Next = red;
            Assert.True(session.Start());
            await NextFrame(session);
            var shown = Pixels(Shown(view));
            Assert.Equal(red.Bgra.ToArray(), shown);

            var straight = Straight();
            backend.Next = straight;
            await NextFrame(session);
            Assert.Equal(Premultiplied(straight), Pixels(Shown(view)));

            session.Stop();
            Assert.Null(ImageOf(view).Source);

            backend.Next = red;
            Assert.True(session.Start());
            await NextFrame(session);
            var bitmap = Shown(view);
            var before = Pixels(bitmap);

            var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.Unloaded += (_, _) => unloaded.TrySetResult();
            popup.Content = new Grid();
            await unloaded.Task.WaitAsync(TestContext.Current.CancellationToken);

            var blue = Solid(0, 0, 255);
            backend.Next = blue;
            await NextFrame(session);
            Assert.Equal(before, Pixels(bitmap));
            Assert.Equal(blue, session.Frame);
            return true;
        });

        Assert.True(kept);
    }

    [Fact]
    public async Task A_capture_view_still_shows_a_frame_after_a_collection()
    {
        var shown = await ShellFixture.On(async shell =>
        {
            var screen = shell.Screens[0].Geometry;
            var backend = new FakeCapture();
            using var session = new CaptureSession(shell.Loop, () => backend)
            {
                FrameInterval = 16,
                Target = CaptureTarget.ForWindow(1),
            };
            var view = new CaptureView
            {
                Width = 80,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Session = session,
            };
            using var popup = new PopupWindow
            {
                Rect = new QRect(screen.X + 80, screen.Y + 80, 200, 120),
                Content = new Grid { Children = { view } },
            };
            popup.Visible = true;
            await ShellFixture.LaidOut(view);

            // The wrapper of a XamlRoot does not live as long as the native object it wraps.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var found = ReferenceEquals(ShellWindow.Of(view), popup);
            var red = Solid(255, 0, 0);
            backend.Next = red;
            Assert.True(session.Start());
            await NextFrame(session);
            var pixels = Pixels(Shown(view));
            return (found, same: pixels.AsSpan().SequenceEqual(red.Bgra));
        });

        Assert.True(shown.found);
        Assert.True(shown.same);
    }

    private static async Task NextFrame(CaptureSession session)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady() => ready.TrySetResult();
        session.FrameReady += OnReady;
        try
        {
            await ready.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            session.FrameReady -= OnReady;
        }
    }

    private static PixelImage Solid(byte red, byte green, byte blue)
    {
        var bgra = new byte[4 * 2 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (blue, green, red, 255);
        return new PixelImage(4, 2, bgra, premultiplied: true);
    }

    private static PixelImage Straight()
    {
        var bgra = new byte[4 * 2 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (0, 0, 255, 128);
        return new PixelImage(4, 2, bgra, premultiplied: false);
    }

    private static byte[] Premultiplied(PixelImage image)
    {
        var pixels = image.Bgra.ToArray();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha == 255)
                continue;
            pixels[i] = (byte)((pixels[i] * alpha + 127) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * alpha + 127) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * alpha + 127) / 255);
        }
        return pixels;
    }

    private static Image ImageOf(CaptureView view) => Assert.IsType<Image>(VisualTreeHelper.GetChild(view, 0));

    private static WriteableBitmap Shown(CaptureView view) => Assert.IsType<WriteableBitmap>(ImageOf(view).Source);

    private static byte[] Pixels(WriteableBitmap bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        using var stream = bitmap.PixelBuffer.AsStream();
        Assert.Equal(bytes.Length, (int)stream.Length);
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed class FakeCapture : ICaptureBackend
    {
        public PixelImage? Next { get; set; }

        public void Start(CaptureTarget target, bool includeCursor, Action closed)
        {
        }

        public PixelImage? ReadLatest()
        {
            var next = Next;
            Next = null;
            return next;
        }

        public void Dispose()
        {
        }
    }
}
