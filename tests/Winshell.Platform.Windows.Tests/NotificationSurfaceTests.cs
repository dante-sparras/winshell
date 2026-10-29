using SkiaSharp;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

[Collection(ThemeCollection.Name)]
public sealed class NotificationSurfaceTests
{
    [Fact]
    public void Actual_qml_groups_and_dismisses_notifications_through_accessible_buttons()
    {
        using var surface = new Surface("NotificationCenter.qml", 352, 320);
        surface.Listener.Apply(Snapshot());
        surface.Root.WriteProperty("listener", surface.Listener);
        surface.Flush();
        var tree = surface.Tree();
        Assert.Contains(Nodes(tree.Root), n => n.Name == "Mail");
        Assert.Contains(Nodes(tree.Root), n => n.Name == "Chat");
        Assert.Contains(Nodes(tree.Root), n => n.Name == "Do not disturb: On · Focus: Off");
        var lastDismiss = Nodes(tree.Root).Last(n => n.Name.StartsWith("Dismiss ", StringComparison.Ordinal));
        lastDismiss.Item.ForceActiveFocus();
        surface.Flush();
        var scroll = surface.Scene.Root.ChildItems.SelectMany(Items).OfType<ScrollView>().Single();
        Assert.True(scroll.Flickable.ContentY > 0);
        Assert.InRange(lastDismiss.Item.MapToScene(0, 0).Y + lastDismiss.Item.Height, 0, 320);
        Save(surface.Scene, "notifications-after", 352, 320);
        var dismiss = Assert.Single(Nodes(tree.Root), n => n.Name == "Dismiss Build finished");
        Assert.True(dismiss.IsKeyboardFocusable);
        dismiss.Invoke();
        surface.Flush();
        Assert.Equal(new uint[] { 2 }, surface.Backend.Dismissed);
        tree.Refresh();
        var groupClear = Assert.Single(Nodes(tree.Root), n => n.Name == "Clear notifications from Mail");
        groupClear.Invoke();
        surface.Flush();
        Assert.Single(surface.Listener.Notifications.Rows);
        tree.Refresh();
        var clearAll = Assert.Single(Nodes(tree.Root), n => n.Name == "Clear all");
        clearAll.Invoke();
        surface.Flush();
        Assert.Empty(surface.Listener.Notifications.Rows);
        Assert.Empty(surface.Warnings);
    }

    [Theory]
    [InlineData(NotificationAccessStatus.Unspecified, "Allow notification access")]
    [InlineData(NotificationAccessStatus.Requesting, "Waiting for Windows permission…")]
    [InlineData(NotificationAccessStatus.Denied, "Notification access was denied.")]
    [InlineData(NotificationAccessStatus.Unavailable, "Notification access is unavailable.")]
    [InlineData(NotificationAccessStatus.Allowed, "You're all caught up")]
    public void Permission_empty_and_unknown_states_are_honest(NotificationAccessStatus status, string expected)
    {
        using var surface = new Surface("NotificationCenter.qml", 352, 320);
        surface.Listener.Apply(new(status, [], NotificationState.Unknown, NotificationMode.Unknown));
        surface.Root.WriteProperty("listener", surface.Listener);
        surface.Flush();
        var nodes = Nodes(surface.Tree().Root).ToArray();
        Assert.Contains(nodes, n => n.Name.StartsWith(expected, StringComparison.Ordinal));
        Assert.Contains(nodes, n => n.Name == "Do not disturb: Unknown · Focus: Unknown");
        Assert.False(Assert.Single(nodes, n => n.Name == "Clear all").IsEnabled);
        Assert.Empty(surface.Warnings);
    }

    [Fact]
    public void Access_is_requested_only_by_the_explicit_allow_button()
    {
        using var surface = new Surface("NotificationCenter.qml", 352, 320);
        surface.Root.WriteProperty("listener", surface.Listener);
        surface.Flush();
        Assert.Equal(0, surface.Backend.Requests);
        var allow = Assert.Single(Nodes(surface.Tree().Root), n => n.Name == "Allow notification access");
        allow.Invoke();
        surface.Flush();
        Assert.Equal(1, surface.Backend.Requests);
        Assert.Equal((int)NotificationAccessStatus.Allowed, surface.Listener.AccessStatus);
        Assert.Empty(surface.Warnings);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1.5)]
    [InlineData(true, true, 2)]
    public void Calendar_layout_and_render_follow_theme_and_scale(bool dark, bool contrast, double scale)
    {
        var saved = Theme.Current;
        try
        {
            Theme.Current = Theme.Default(dark) with { IsHighContrast = contrast };
            using var surface = new Surface("CalendarView.qml", 352, 360);
            surface.Root.WriteProperty("localeName", "sv-SE");
            surface.Root.WriteProperty("date", new DateTime(2024, 2, 29));
            surface.Root.WriteProperty("showWeekNumbers", true);
            surface.Flush();
            var nodes = Nodes(surface.Tree().Root).ToArray();
            var days = nodes.Where(n => n.Item is Button && n.Name.Contains("2024", StringComparison.Ordinal)).ToArray();
            Assert.Equal(42, days.Length);
            Assert.All(days, n =>
            {
                Assert.True(n.IsKeyboardFocusable);
                Assert.InRange(n.Bounds.X, 28, 352 - 40);
                Assert.InRange(n.Bounds.Right, 40, 352.01);
                Assert.Equal(36, n.Bounds.Height);
            });
            Assert.Equal(6, nodes.Count(n => n.Item is Text && n.Name.StartsWith("Week ", StringComparison.Ordinal)));
            var today = Assert.Single(days, d => d.Description.Contains("Today.", StringComparison.Ordinal));
            var next = Assert.Single(nodes, n => n.Name == "Next month");
            next.Item.ForceActiveFocus();
            surface.Scene.KeyPress(new KeyEvent { Key = 0x20 });
            surface.Scene.KeyRelease(new KeyEvent { Key = 0x20 });
            surface.Flush();
            Assert.Equal(1, ((CalendarMonth)surface.Root.ReadProperty("month")!).MonthOffset);
            ((CalendarMonth)surface.Root.ReadProperty("month")!).MonthOffset = 0;
            surface.Flush();
            using var bitmap = new SKBitmap((int)(352 * scale), (int)(360 * scale));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(Theme.Current.SolidBackgroundFillColorBase.ToSk());
            canvas.Scale((float)scale);
            surface.Scene.Paint(canvas, TimeSpan.Zero);
            var sample = bitmap.GetPixel((int)((today.Bounds.X + 5) * scale), (int)((today.Bounds.Y + 18) * scale));
            Assert.Equal(FluentResources.AccentButtonBackground(Theme.Current).Rest.ToSk(), sample);
            // Snapshot the real QML renderer to a caller-owned evidence folder when explicitly requested.
            if (Environment.GetEnvironmentVariable("WINSHELL_NOTIFICATION_EVIDENCE") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(folder, $"calendar-{dark}-{contrast}-{scale}.png"), png.ToArray());
            }
            Assert.Empty(surface.Warnings);
        }
        finally { Theme.Current = saved; }
    }

    [Fact]
    public void Clock_hook_opens_the_real_flyout_and_escape_dismisses_it()
    {
        using var surface = new Surface("TaskbarClock.qml", 200, 48);
        var clock = Assert.Single(Nodes(surface.Tree().Root), n => n.Description == "Open notification center and calendar");
        Assert.True(clock.IsKeyboardFocusable);
        Assert.InRange(clock.Bounds.Y, 0, 48);
        Assert.InRange(clock.Bounds.Bottom, 1, 48);
        clock.Invoke();
        surface.Flush();
        var flyout = Assert.IsType<PopupWindow>(surface.Root.CreationContext!.FindId("calendar"));
        Assert.True(flyout.Visible);
        Assert.True(flyout.GrabFocus);
        Assert.Equal(WindowBackdrop.Acrylic, flyout.Backdrop);
        flyout.OnResized(384, 780, 1.5);
        surface.Flush();
        Assert.Equal("Close", Assert.IsType<Button>(flyout.Scene.FocusItem).Text);
        Assert.True(flyout.Scene.KeyPress(new KeyEvent { Key = 0x01000000 }));
        surface.Flush();
        Assert.False(flyout.Visible);
        Assert.Empty(surface.Warnings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Flyout_fits_a_secondary_work_area_without_scaling_its_physical_corner(double scale)
    {
        var bounds = new QRect(1920, 0, 1920 / scale, 1040 / scale);
        var width = Math.Min(384, bounds.Width);
        var height = Math.Min(780, bounds.Height - 16);
        var rect = PopupWindow.Place(new QRect(bounds.Right - 100, 0, 100, 40), width, height,
            Edges.Bottom | Edges.Right, Edges.Bottom | Edges.Left, 63, bounds);
        Assert.InRange(rect.X, bounds.X, bounds.Right);
        Assert.InRange(rect.Right, bounds.X, bounds.Right + 1);
        Assert.InRange(rect.Y, bounds.Y, bounds.Bottom);
        Assert.InRange(rect.Bottom, bounds.Y, bounds.Bottom + 1);
    }

    [Fact]
    public void Evidence_renders_the_complete_surface_with_synthetic_notifications_only()
    {
        using var surface = new Surface("CalendarFlyout.qml", 384, 780);
        surface.Listener.Apply(Snapshot());
        surface.Root.WriteProperty("listener", surface.Listener);
        surface.Root.WriteProperty("date", new DateTime(2026, 10, 29));
        surface.Flush();
        var calendar = surface.Root.CreationContext!.FindId("calendar")!;
        var month = (CalendarMonth)calendar.ReadProperty("month")!;
        Assert.Equal(0, month.MonthOffset);
        Assert.Equal(new DateTime(2026, 10, 29), month.Date);
        Assert.Contains("2026", month.MonthLabel);
        Save(surface.Scene, "flyout-after", 384, 780);
        if (Environment.GetEnvironmentVariable("WINSHELL_NOTIFICATION_EVIDENCE") is { Length: > 0 } folder
            && File.Exists(Path.Combine(folder, "BeforeCalendar.qml")))
        {
            using var before = new Surface(Path.Combine(folder, "BeforeCalendar.qml"), 336, 400);
            before.Root.WriteProperty("date", new DateTime(2026, 10, 29));
            before.Flush();
            Save(before.Scene, "flyout-before", 336, 400);
            Assert.Empty(before.Warnings);
        }
        Assert.Empty(surface.Warnings);
    }

    private static IEnumerable<Item> Items(Item item) => new[] { item }.Concat(item.ChildItems.SelectMany(Items));

    private static void Save(Scene scene, string name, int width, int height)
    {
        if (Environment.GetEnvironmentVariable("WINSHELL_NOTIFICATION_EVIDENCE") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Theme.Current.SolidBackgroundFillColorBase.ToSk());
        scene.Paint(canvas, TimeSpan.Zero);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), png.ToArray());
    }

    private static IEnumerable<AccessibleNode> Nodes(AccessibleNode node) => node.Children.SelectMany(n => new[] { n }.Concat(Nodes(n)));
    private static NotificationSnapshot Snapshot() => new(NotificationAccessStatus.Allowed,
        [new(1, "mail", "Mail", "New mail", "A longer message that wraps within the notification card. " + new string('a', 120), Created),
         new(2, "mail", "Mail", "Build finished", "Open report", Created),
         new(3, "chat", "Chat", "Hello", "World", Created)], NotificationState.Off, NotificationMode.PriorityOnly);
    private static readonly DateTimeOffset Created = new(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);

    private sealed class Backend : INotificationBackend
    {
        public List<uint> Dismissed { get; } = [];
        public int Requests { get; private set; }
        public Task<NotificationAccessStatus> RequestAccessAsync() { Requests++; return Task.FromResult(NotificationAccessStatus.Allowed); }
        public Task<NotificationSnapshot> ReadAsync() => Task.FromResult(Snapshot());
        public void Dismiss(uint id) => Dismissed.Add(id);
    }

    private sealed class Surface : IDisposable
    {
        private readonly QmlEngine _engine;
        public ManualEventLoop Loop { get; } = new();
        public List<string> Warnings { get; } = [];
        public Backend Backend { get; } = new();
        public NotificationListener Listener { get; }
        public QObject Root { get; }
        public Scene Scene { get; }
        public Surface(string file, double width, double height)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "DefaultShell");
            var modules = StandardModules.Create();
            WindowsModules.Register(modules);
            ServiceModules.Register(modules);
            new DefaultShell.FakeServices().Register(modules);
            _engine = new QmlEngine(modules, Loop, new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) Warnings.Add($"{category}: {message}"); } }, folder);
            Listener = new NotificationListener(Backend) { Engine = _engine };
            Root = _engine.CreateRoot(Path.Combine(folder, file));
            if (Root is ShellWindow window)
            {
                Scene = window.Scene;
                window.OnResized(width, height, 1);
            }
            else
            {
                Scene = new Scene();
                Scene.Root.SetLayoutWidth(width);
                Scene.Root.SetLayoutHeight(height);
                var item = (Item)Root;
                item.Width = width;
                item.Height = height;
                item.SetParentItem(Scene.Root);
            }
            Flush();
        }
        public void Flush() { Loop.Process(); PolishQueue.Flush(); Loop.Process(); PolishQueue.Flush(); }
        public AccessibleTree Tree() { var tree = new AccessibleTree(Scene); tree.Refresh(); return tree; }
        public void Dispose() { Listener.Destroy(); _engine.Dispose(); }
    }
}

