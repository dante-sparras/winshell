using System.Globalization;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Notification times use the process short-time pattern, the same one as the taskbar clock. The weekday is kept
/// only when that local day is no longer today.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class NotificationTimeFormatTests
{
    private static readonly DateTime Clock = new(2026, 11, 15, 9, 41, 0, DateTimeKind.Local);

    [Fact]
    public void Times_follow_the_regional_short_time_and_name_an_earlier_day()
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            AssertTimes(Format("h:mm tt"), "3:45 PM", "12:05 AM", "9:30 AM");
            AssertTimes(Format("HH:mm"), "15:45", "00:05", "09:30");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }

    private static void AssertTimes(CultureInfo format, string afternoon, string midnight, string morning)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = format;
        var earlier = new DateTime(2026, 11, 14, 23, 50, 0, DateTimeKind.Local);
        var earlierText = format.DateTimeFormat.GetAbbreviatedDayName(earlier.DayOfWeek) + " " + earlier.ToString("t", format);
        using var surface = new Surface(FixedClock(Clock));
        surface.Listener.Apply(new NotificationSnapshot(NotificationAccessStatus.Allowed,
        [
            new(1, "mail", "Mail", "Afternoon", "Hello", At(new DateTime(2026, 11, 15, 15, 45, 0, DateTimeKind.Local))),
            new(2, "mail", "Mail", "Midnight", "Hello", At(new DateTime(2026, 11, 15, 0, 5, 0, DateTimeKind.Local))),
            new(3, "mail", "Mail", "Morning", "Hello", At(new DateTime(2026, 11, 15, 9, 30, 0, DateTimeKind.Local))),
            new(4, "mail", "Mail", "Earlier", "Hello", At(earlier)),
        ], NotificationState.Off, NotificationMode.Unrestricted));
        surface.Root.WriteProperty("listener", surface.Listener);
        surface.Flush();
        var names = Nodes(surface.Tree().Root).Select(n => n.Name).ToArray();
        Assert.Contains(afternoon, names);
        Assert.Contains(midnight, names);
        Assert.Contains(morning, names);
        Assert.Contains(earlierText, names);
        Assert.DoesNotContain(format.DateTimeFormat.GetAbbreviatedDayName(Clock.DayOfWeek) + " " + afternoon, names);
        Assert.Empty(surface.Warnings);
    }

    private static CultureInfo Format(string shortTime)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortTimePattern = shortTime;
        culture.DateTimeFormat.AMDesignator = "AM";
        culture.DateTimeFormat.PMDesignator = "PM";
        return CultureInfo.ReadOnly(culture);
    }

    private static DateTimeOffset At(DateTime local) => new(local, TimeZoneInfo.Local.GetUtcOffset(local));

    private static FakeTimeProvider FixedClock(DateTime local) => new(At(local).ToUniversalTime());

    private static IEnumerable<AccessibleNode> Nodes(AccessibleNode node) => node.Children.SelectMany(n => new[] { n }.Concat(Nodes(n)));

    private sealed class Backend : INotificationBackend
    {
        public Task<NotificationAccessStatus> RequestAccessAsync() => Task.FromResult(NotificationAccessStatus.Allowed);
        public Task<NotificationSnapshot> ReadAsync() => Task.FromResult(new NotificationSnapshot(NotificationAccessStatus.Allowed, [], NotificationState.Off, NotificationMode.Unrestricted));
        public void Dismiss(uint id) { }
    }

    private sealed class Surface : IDisposable
    {
        private readonly QmlEngine _engine;
        private readonly ManualEventLoop _loop = new();
        public List<string> Warnings { get; } = [];
        public NotificationListener Listener { get; }
        public QObject Root { get; }
        public Scene Scene { get; }

        public Surface(TimeProvider clock)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "DefaultShell");
            var modules = StandardModules.Create();
            WindowsModules.Register(modules);
            ServiceModules.Register(modules);
            new DefaultShell.FakeServices().Register(modules);
            _engine = new QmlEngine(modules, _loop, new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) Warnings.Add($"{category}: {message}"); } }, folder);
            _engine.AddService(clock);
            Listener = new NotificationListener(new Backend()) { Engine = _engine };
            Root = _engine.CreateRoot(Path.Combine(folder, "NotificationCenter.qml"));
            Scene = new Scene();
            Scene.Root.SetLayoutWidth(352);
            Scene.Root.SetLayoutHeight(640);
            var item = (Item)Root;
            item.Width = 352;
            item.Height = 640;
            item.SetParentItem(Scene.Root);
            Flush();
        }

        public void Flush() { _loop.Process(); PolishQueue.Flush(); _loop.Process(); PolishQueue.Flush(); }
        public AccessibleTree Tree() { var tree = new AccessibleTree(Scene); tree.Refresh(); return tree; }
        public void Dispose() { Listener.Destroy(); _engine.Dispose(); }
    }
}
