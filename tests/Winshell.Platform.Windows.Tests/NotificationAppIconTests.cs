using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class NotificationAppIconTests
{
    [Fact]
    public void A_logo_is_one_file_per_app_and_goes_away_with_its_last_notification()
    {
        var app = NewApp();
        var png = Png(3, 2);
        var opens = 0;
        var fake = new FakeLogo(_ =>
        {
            Interlocked.Increment(ref opens);
            return new MemoryStream(png);
        });
        var log = QuietLog();

        var first = NotificationAppIcons.Read(app, "Mail", fake, log, CancellationToken.None);
        var second = NotificationAppIcons.Read(app, "Mail", fake, log, CancellationToken.None);
        try
        {
            Assert.Equal(1, opens);
            Assert.False(string.IsNullOrEmpty(first.Url));
            Assert.Equal(first.Url, second.Url);
            Assert.StartsWith("file:///", first.Url, StringComparison.Ordinal);
            var path = new Uri(first.Url).LocalPath;
            Assert.Contains(
                Path.Combine("notification-icons", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)),
                path,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(path));

            NotificationAppIcons.Release(first.Hold);
            Assert.True(File.Exists(path));
            NotificationAppIcons.Release(second.Hold);
            Assert.False(File.Exists(path));
        }
        finally
        {
            NotificationAppIcons.Release(first.Hold);
            NotificationAppIcons.Release(second.Hold);
        }
    }

    [Fact]
    public void No_logo_is_empty_and_is_not_logged()
    {
        var messages = new List<string>();
        var read = NotificationAppIcons.Read(NewApp(), "Mail", new FakeLogo(_ => null), Log(messages), CancellationToken.None);
        NotificationAppIcons.Release(read.Hold);

        Assert.Equal("", read.Url);
        Assert.Empty(messages);
    }

    [Fact]
    public void A_failed_read_is_empty_and_logged_once_per_app()
    {
        var messages = new List<string>();
        var app = NewApp();
        var opens = 0;
        var fake = new FakeLogo(_ =>
        {
            Interlocked.Increment(ref opens);
            throw new IOException("gone");
        });
        var log = Log(messages);

        var first = NotificationAppIcons.Read(app, "Mail", fake, log, CancellationToken.None);
        NotificationAppIcons.Release(first.Hold);
        var second = NotificationAppIcons.Read(app, "Mail", fake, log, CancellationToken.None);
        NotificationAppIcons.Release(second.Hold);

        Assert.Equal("", first.Url);
        Assert.Equal("", second.Url);
        Assert.Equal(2, opens);
        Assert.Equal(["notifications: Cannot read the icon from Mail: gone"], messages);
    }

    [Fact]
    public void A_logo_that_is_not_a_picture_or_is_over_the_size_cap_is_empty()
    {
        var messages = new List<string>();
        var log = Log(messages);
        var junk = NotificationAppIcons.Read(NewApp(), "Mail", new FakeLogo(_ => new MemoryStream([1, 2, 3, 4])), log, CancellationToken.None);
        NotificationAppIcons.Release(junk.Hold);
        var huge = NotificationAppIcons.Read(NewApp(), "Other", new FakeLogo(_ => new OversizedStream()), log, CancellationToken.None);
        NotificationAppIcons.Release(huge.Hold);

        Assert.Equal("", junk.Url);
        Assert.Equal("", huge.Url);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Contains("not a picture under the size limit", message, StringComparison.Ordinal));
    }

    [Fact]
    public void The_icon_fills_in_after_the_notification_and_a_disposed_one_stays_empty()
    {
        var appId = NewApp();
        var opens = 0;
        var opened = new ManualResetEventSlim(false);
        var gate = new ManualResetEventSlim(false);
        var fake = new FakeLogo(_ =>
        {
            Interlocked.Increment(ref opens);
            opened.Set();
            gate.Wait();
            return new MemoryStream(Png(2, 2));
        });
        var warnings = new List<string>();
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop, Log(warnings));
        var listener = new NotificationListener(new FakeBackend());
        listener.Engine = engine;
        var posted = 0;
        var done = new ManualResetEventSlim(false);
        loop.Woken += () =>
        {
            if (Interlocked.Increment(ref posted) == 2)
                done.Set();
        };
        try
        {
            listener.Apply(Snapshot(Toast(1, appId, fake), Toast(2, appId, fake)));
            opened.Wait();
            var rows = listener.Notifications.Rows.Cast<ShellNotification>().OrderBy(n => n.Id).ToArray();
            Assert.Equal("", rows[0].AppIcon);
            Assert.Equal("", rows[1].AppIcon);
            var changes = 0;
            rows[1].Connect("appIconChanged", _ => changes++);

            Assert.True(rows[0].Dismiss());
            gate.Set();
            done.Wait();
            loop.Process();

            Assert.True(rows[0].IsDestroyed);
            Assert.Equal("", rows[0].AppIcon);
            Assert.StartsWith("file:///", rows[1].AppIcon, StringComparison.Ordinal);
            Assert.Equal(1, changes);
            Assert.Equal(1, opens);
            var path = new Uri(rows[1].AppIcon).LocalPath;
            Assert.True(File.Exists(path));

            Assert.True(rows[1].Dismiss());
            Assert.False(File.Exists(path));
            Assert.Empty(warnings);
        }
        finally
        {
            gate.Set();
            if (!done.IsSet)
                done.Wait();
            loop.Process();
            listener.Destroy();
            loop.Process();
            opened.Dispose();
            gate.Dispose();
            done.Dispose();
        }
    }

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
        var outside = Path.Combine(elsewhere, "old.png");
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

            NotificationAppIcons.CleanOld(linkedRoot ? link : cache, Path.Combine(directory, "keep"));

            Assert.True(File.Exists(outside), "Cleanup followed a junction and removed a file outside the cache.");
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IncomingToast Toast(uint id, string appId, IAppLogoSource logo) =>
        new(id, appId, "Mail", "Hello", "A message", DateTimeOffset.UnixEpoch, Logo: logo);

    private static NotificationSnapshot Snapshot(params IncomingToast[] toasts) =>
        new(NotificationAccessStatus.Allowed, toasts, NotificationState.Off, NotificationMode.Unrestricted);

    private static string NewApp() => "Mail." + Guid.NewGuid().ToString("N");

    private static Log QuietLog() => new() { Sink = static (_, _, _) => { } };

    private static Log Log(List<string> messages) => new() { Sink = (_, category, message) => messages.Add($"{category}: {message}") };

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.Black);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Reads as <see cref="NotificationAppIcons.MaxBytes"/> plus one byte, without keeping that buffer.</summary>
    private sealed class OversizedStream : Stream
    {
        private int _left = NotificationAppIcons.MaxBytes + 1;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left == 0)
                return 0;
            var n = Math.Min(count, _left);
            _left -= n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeLogo(Func<CancellationToken, Stream?> open) : IAppLogoSource
    {
        public Stream? Open(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return open(cancellation);
        }
    }

    private sealed class FakeBackend : INotificationBackend
    {
        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);
        public Task<NotificationSnapshot> ReadAsync() => Task.FromResult(Snapshot());
        public void Dismiss(uint id) { }
    }
}
