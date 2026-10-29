using System.Globalization;
using SkiaSharp;
using Winshell.Platform.Windows.Services.Network;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Platform.Windows.Services.Power;
using Winshell.Platform.Windows.Tests.DefaultShell;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.Snapshots;

/// <summary>
/// The whole default shell on one screen at a fixed scale, against fake services with the same apps, windows,
/// devices and notifications every time. The time is fixed at Thursday 29 October 2026, 09:41, the format is
/// en-US, the theme is the Windows default blue accent in light or dark, and animations are off. Its folders are
/// temporary, so nothing of this PC's shell is read or written.
/// </summary>
internal sealed class SnapshotShell : IDisposable
{
    /// <summary>The wall-clock time every snapshot shows.</summary>
    public static readonly DateTime Now = new(2026, 10, 29, 9, 41, 0, DateTimeKind.Local);

    /// <summary>
    /// en-US with its date and time patterns written out, because the ICU that Windows ships changes them between
    /// releases (a newer one puts a narrow no-break space before AM).
    /// </summary>
    private static readonly CultureInfo Format = MakeFormat();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-snapshot-tests", Guid.NewGuid().ToString("N"));
    private readonly Theme _theme = Theme.Current;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly NotificationListener _listener;

    public SnapshotShell(bool dark, double scale)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Format;
            Theme.Current = Theme.Default(dark) with { AnimationsEnabled = false };
            Platform = new SnapshotPlatform(scale);

            var shell = Path.Combine(_directory, "shell");
            var data = Path.Combine(_directory, "data");
            Directory.CreateDirectory(shell);
            Directory.CreateDirectory(data);
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "DefaultShell")))
                File.Copy(file, Path.Combine(shell, Path.GetFileName(file)));
            var root = File.ReadAllText(Path.Combine(shell, "shell.qml"));
            // A shell id and folders of its own, and a listener the test fills instead of Windows.
            root = Replace(root, "//@ pragma ShellId winshell-default", $"""
                //@ pragma ShellId winshell-snapshot-{Guid.NewGuid():N}
                //@ pragma DataDir {data}
                //@ pragma StateDir {Path.Combine(_directory, "state")}
                //@ pragma CacheDir {Path.Combine(_directory, "cache")}
                """);
            root = Replace(root, "NotificationListener { id: notifications }", "property var notifications: null");
            File.WriteAllText(Path.Combine(shell, "shell.qml"), root);

            var icons = Path.Combine(_directory, "icons");
            Directory.CreateDirectory(icons);
            string Icon(string name, SKColor color) => SnapshotIcons.Write(Path.Combine(icons, name + ".png"), color);
            var services = new FakeServices();
            var mail = services.Apps.Add("Contoso.Mail", "Mail", "Contoso.Mail", Icon("mail", new SKColor(0x00, 0x78, 0xD4)));
            var notes = services.Apps.Add("Contoso.Notes", "Notes", "Contoso.Notes", Icon("notes", new SKColor(0xF7, 0xB5, 0x00)));
            var photos = services.Apps.Add("Contoso.Photos", "Photos", "Contoso.Photos", Icon("photos", new SKColor(0x8E, 0x56, 0xC2)));
            var terminal = services.Apps.Add("Contoso.Terminal", "Terminal", "Contoso.Terminal", Icon("terminal", new SKColor(0x30, 0x30, 0x30)));
            services.Apps.Add("Contoso.Maps", "Maps", "Contoso.Maps", Icon("maps", new SKColor(0x10, 0x89, 0x3E)));
            var calculator = services.Apps.Add("Contoso.Calculator", "Calculator", "Contoso.Calculator", Icon("calculator", new SKColor(0x5C, 0x2D, 0x91)));
            var weather = services.Apps.Add("Contoso.Weather", "Weather", "Contoso.Weather", Icon("weather", new SKColor(0x00, 0x99, 0xBC)));
            // Recommended leaves out what is pinned: Photos is, the other two are not.
            services.Apps.Recent.Add(photos);
            services.Apps.Recent.Add(calculator);
            services.Apps.Recent.Add(weather);
            File.WriteAllText(Path.Combine(data, "taskbar.json"), """{ "pinned": ["Contoso.Mail", "Contoso.Notes"] }""");
            File.WriteAllText(Path.Combine(shell, "launcher.json"), """{ "pins": ["Contoso.Mail", "Contoso.Notes", "Contoso.Photos", "Contoso.Terminal", "Contoso.Maps"] }""");
            var inbox = new FakeAppWindow("Inbox - Mail", "Contoso.Mail", mail.Icon);
            var shellWindow = new FakeAppWindow("Windows PowerShell", "Contoso.Terminal", terminal.Icon);
            services.WindowList.Windows.Add(inbox);
            services.WindowList.Windows.Add(shellWindow);
            services.WindowList.Activate(inbox);

            var wifi = new FakeNetworkDevice(DeviceType.Wifi, new FakeNetwork("Home") { Connected = true }) { Connected = true };
            services.Networking.Devices.Add(wifi);
            services.Networking.WifiEnabled = true;
            services.Networking.WifiState = WifiRadioState.On;
            services.Networking.Connectivity = NetworkConnectivity.InternetAccess;
            services.Bluetooth.DefaultAdapter = new FakeBluetoothAdapter { Enabled = true };
            services.Bluetooth.Devices.Add(new FakeBluetoothDevice("Headphones") { Connected = true });
            services.Power.DisplayDevice.IsPresent = true;
            services.Power.DisplayDevice.Percentage = 0.76;
            services.Power.DisplayDevice.State = PowerDeviceState.Discharging;
            services.Brightness.Monitors.Add(new FakeBrightnessMonitor(BrightnessConnection.Internal, 0, 100, "DISPLAY1", "Built-in display") { Brightness = 60 });
            Speakers = new FakeAudioNode("Speakers");
            Speakers.Audio.Volume = 0.3;
            services.Audio.Nodes.Add(Speakers);
            services.Audio.DefaultPlaybackDevice = Speakers;
            services.Media.Sessions.Add(new FakeMediaSession("Contoso Music", "Northern Lights", "The Examples") { IsPlaying = true });
            Services = services;

            // As the shell's process does: icons come from the Windows icon provider, which reads these images as they are.
            ImageLoader.AddProvider("icon", IconProvider.Instance);
            var log = new Log { Sink = (_, category, message) => Messages.Add($"{category}: {message}"), MinimumLevel = LogLevel.Warn };
            Host = new ShellHost(Path.Combine(shell, "shell.qml"), Loop, Platform, log, () =>
            {
                var registry = StandardModules.Create();
                WindowsModules.Register(registry);
                ServiceModules.Register(registry);
                services.Register(registry);
                return registry;
            }, engine =>
            {
                engine.AddService<TimeProvider>(new FixedTime(Now));
                engine.AddService<IIconLookup>(IconProvider.Instance);
            })
            { ConfigRoot = Path.Combine(_directory, "config") };
            Host.WatchFiles = false;
            Assert.True(Host.Start(), string.Join(Environment.NewLine, Messages));
            _listener = new NotificationListener(new FakeNotifications()) { Engine = Host.Engine };
            _listener.Apply(FakeNotifications.Snapshot);
            Host.Root!.WriteProperty("notifications", _listener);
            Settle();
            // The on-screen display starts from what the services report, and only shows what changes after that.
            Loop.Advance(TimeSpan.FromSeconds(1));
            Settle();
        }
        catch
        {
            // A shell that does not start leaves nothing behind for the next test.
            try
            {
                Host?.Dispose();
            }
            finally
            {
                Restore();
            }
            throw;
        }
    }

    public SnapshotPlatform Platform { get; }

    public ManualEventLoop Loop { get; } = new();

    public ShellHost Host { get; }

    public FakeServices Services { get; }

    public FakeAudioNode Speakers { get; }

    public List<string> Messages { get; } = [];

    public PanelWindow Taskbar => Platform.Windows.Select(w => w.Client).OfType<PanelWindow>().Single(p => p.HasProperty("centered"));

    public SnapshotWindow Backend(ShellWindow window) => Platform.Windows.Single(w => w.Client == window);

    /// <summary>The window a surface shows in, opened the way a user opens it.</summary>
    public ShellWindow Open(string surface)
    {
        switch (surface)
        {
            case "taskbar":
                return Taskbar;
            case "start":
                Click(Taskbar, Walk(Taskbar.ContentItem).Single(i => i.ObjectName == "startButton"));
                return VisiblePopup();
            case "quick-settings":
                Click(Taskbar, Walk(Taskbar.ContentItem).OfType<Button>().Single(b => b.Text == "Quick settings"));
                return VisiblePopup();
            case "notification-center":
                Click(Taskbar, Walk(Taskbar.ContentItem).OfType<Button>().Single(b => b.Text == "9:41 AM\n10/29/2026"));
                return VisiblePopup();
            case "osd":
                Speakers.Audio.Volume = 0.64;
                Settle();
                var osd = Platform.Windows.Select(w => w.Client).OfType<PanelWindow>().Single(p => p.ObjectName == "osd");
                Assert.True(osd.Visible, "The on-screen display did not show the volume change.");
                return osd;
            case "settings":
                Assert.Equal("", Host.Ipc.Call("settings", "open", [""]));
                Settle();
                return Platform.Windows.Select(w => w.Client).OfType<FloatingWindow>().Single(w => w.Visible);
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, "No such surface.");
        }
    }

    public void Settle()
    {
        Loop.Process();
        PolishQueue.Flush();
        Loop.Process();
        PolishQueue.Flush();
    }

    private PopupWindow VisiblePopup() => Platform.Windows.Select(w => w.Client).OfType<PopupWindow>().Single(p => p.Visible);

    private void Click(ShellWindow window, Item item)
    {
        var at = item.MapToScene(item.Width / 2, item.Height / 2);
        window.Scene.PointerPress(at.X, at.Y, QtEnums.LeftButton);
        window.Scene.PointerRelease(at.X, at.Y, QtEnums.LeftButton);
        // The pointer leaves, so nothing is drawn hovered.
        window.Scene.PointerMove(-100, -100);
        Settle();
    }

    private static CultureInfo MakeFormat()
    {
        var culture = new CultureInfo("en-US", useUserOverride: false);
        var format = culture.DateTimeFormat;
        format.ShortTimePattern = "h:mm tt";
        format.LongTimePattern = "h:mm:ss tt";
        format.ShortDatePattern = "M/d/yyyy";
        format.LongDatePattern = "dddd, MMMM d, yyyy";
        format.YearMonthPattern = "MMMM yyyy";
        format.AMDesignator = "AM";
        format.PMDesignator = "PM";
        format.FirstDayOfWeek = DayOfWeek.Sunday;
        return CultureInfo.ReadOnly(culture);
    }

    private static IEnumerable<Item> Walk(Item item) => item.ChildItems.SelectMany(Walk).Prepend(item);

    private static string Replace(string text, string from, string to)
    {
        Assert.True(text.Contains(from, StringComparison.Ordinal), $"shell.qml no longer has \"{from}\".");
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _listener.Destroy();
        Host.Dispose();
        Restore();
    }

    private void Restore()
    {
        Theme.Current = _theme;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A clock that always reads the same local time.</summary>
    private sealed class FixedTime(DateTime local) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Three notifications from two apps, received this morning.</summary>
    private sealed class FakeNotifications : INotificationBackend
    {
        public static NotificationSnapshot Snapshot { get; } = new(NotificationAccessStatus.Allowed,
            [
                new(1, "Contoso.Mail", "Mail", "Megan Bowen", "Lunch on Friday? The new place on the corner has opened, and I booked a table for four at noon.", At(9, 12)),
                new(2, "Contoso.Mail", "Mail", "Build finished", "Release 1.0 is ready to download.", At(9, 30)),
                new(3, "Contoso.Notes", "Notes", "Reminder", "Water the plants", At(8, 0)),
            ], NotificationState.Off, NotificationMode.Unrestricted);

        private static DateTimeOffset At(int hour, int minute)
        {
            var local = new DateTime(Now.Year, Now.Month, Now.Day, hour, minute, 0, DateTimeKind.Local);
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }

        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);

        public Task<NotificationSnapshot> ReadAsync() => Task.FromResult(Snapshot);

        public void Dismiss(uint id)
        {
        }
    }
}

/// <summary>App icons drawn from shapes alone, so they look the same on every PC.</summary>
internal static class SnapshotIcons
{
    public static string Write(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var fill = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(4, 4, 60, 60), 12, 12, fill);
            using var mark = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawCircle(32, 32, 12, mark);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
        return path;
    }
}
