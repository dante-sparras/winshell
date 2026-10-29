using System.Globalization;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.Snapshots;

/// <summary>
/// Winshell's own notice, for a configuration error and for a window that failed, rendered offscreen at 100%, 150%
/// and 200% in light and dark and compared with its baselines (<c>notice-…</c>). The notice is Winshell's, not the
/// default shell's, so it is shown over an empty shell. See <see cref="Snapshot"/> and docs/development.md.
/// </summary>
[Collection(ThemeCollection.Name)]
public sealed class NoticeSnapshotTests
{
    public static TheoryData<string, string, double> Cases()
    {
        var cases = new TheoryData<string, string, double>();
        foreach (var kind in new[] { "config-error", "crash" })
            foreach (var theme in new[] { "light", "dark" })
                foreach (var scale in new[] { 1, 1.5, 2 })
                    cases.Add(kind, theme, scale);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Notice_looks_as_it_did(string kind, string theme, double scale)
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-snapshot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Theme.Current;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            Theme.Current = Theme.Default(theme == "dark") with { AnimationsEnabled = false };
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var root = Path.Combine(dir, "shell.qml");
            File.WriteAllText(root, "import Winshell\nShellRoot {}\n");
            var loop = new ManualEventLoop();
            var platform = new SnapshotPlatform(scale);
            var messages = new List<string>();
            var log = new Log { Sink = (level, category, message) => messages.Add($"{category}: {message}") };
            ModuleRegistry Modules()
            {
                var registry = StandardModules.Create();
                WindowsModules.Register(registry);
                return registry;
            }
            using var host = new ShellHost(root, loop, platform, log, Modules) { WatchFiles = false };
            using var notices = new ShellNotices(host, loop, platform, log, _ => { }, Modules);
            Assert.True(host.Start());

            notices.Show(kind == "config-error"
                ? ShellNotices.ForConfigError(@"C:\Users\you\AppData\Roaming\winshell\bars\Taskbar.qml[42:7]: Cannot assign to non-existent property ""colour""",
                    @"C:\Users\you\AppData\Roaming\winshell\shell.qml", running: true)
                : ShellNotices.ForCrash(new CrashReport(@"C:\crash.txt", "PanelWindow in Taskbar.qml", new InvalidOperationException(), false)
                {
                    Subject = "the taskbar",
                    Recovery = "Winshell opens it again in 1 second.",
                }));
            loop.Process();
            PolishQueue.Flush();
            loop.Process();

            var window = platform.Windows.Single(w => ((ShellWindow)w.Client).ObjectName == "notice");
            using var render = window.Render(Theme.Current.SolidBackgroundFillColorBase.ToSk());
            Snapshot.Match($"notice-{kind}-{theme}-{(scale * 100).ToString(CultureInfo.InvariantCulture)}", render);
            Assert.Empty(messages);
        }
        finally
        {
            Theme.Current = previous;
            CultureInfo.CurrentCulture = culture;
            Directory.Delete(dir, true);
        }
    }
}
