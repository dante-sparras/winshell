using Winshell.Platform.Windows;
using Winshell.Platform.Windows.Services.Input;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>The default shell's taskbar chords: Search, quick settings, notifications, and the numbered apps.</summary>
public sealed class ShellChordTests
{
    [Fact]
    public void The_taskbar_chords_are_registered_on_the_main_taskbar()
    {
        var dir = TempDir();
        try
        {
            foreach (var file in Directory.GetFiles(DefaultShellDir()))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            var registry = StandardModules.Create();
            WindowsModules.Register(registry);
            ServiceModules.Register(registry);
            var loop = new ManualEventLoop();
            using var engine = new QmlEngine(registry, loop, new Log(), dir);
            var root = (ShellRoot)engine.CreateRoot(Path.Combine(dir, "shell.qml"));
            loop.Process();

            var hotkeys = Walk(root).OfType<Hotkey>().ToArray();
            Assert.Equal("Hotkeys are not available.", One(hotkeys, "Win+T").Error);
            Assert.Equal("Hotkeys are not available.", One(hotkeys, "Win+S").Error);
            Assert.Equal("Hotkeys are not available.", One(hotkeys, "Win+A").Error);
            Assert.Equal("Hotkeys are not available.", One(hotkeys, "Win+N").Error);
            foreach (var number in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" })
            {
                Assert.Equal("Hotkeys are not available.", One(hotkeys, "Win+" + number).Error);
                Assert.Equal("Hotkeys are not available.", One(hotkeys, "Win+Shift+" + number).Error);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Hotkey One(IEnumerable<Hotkey> hotkeys, string sequence) =>
        Assert.Single(hotkeys, h => h.Sequence == sequence);

    private static IEnumerable<QObject> Walk(QObject obj)
    {
        yield return obj;
        foreach (var owned in obj.OwnedObjects)
        {
            foreach (var nested in Walk(owned))
                yield return nested;
        }
    }

    private static string DefaultShellDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Winshell", "DefaultShell");
            if (File.Exists(Path.Combine(candidate, "shell.qml")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("DefaultShell");
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-shell-chord-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
