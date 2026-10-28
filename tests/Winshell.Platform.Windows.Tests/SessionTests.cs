using Winshell.Platform.Windows.Services.Session;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// <c>Session.lock()</c> hands the lock to Windows. These tests use a fake and never lock the PC.
/// </summary>
public sealed class SessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winshell-session-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = [];

    public SessionTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Lock_asks_windows_once()
    {
        var windows = new FakeWorkstationLock(accepts: true);

        var root = Load(windows, """
            import QtQuick
            import Winshell
            Item {
                property bool locked: false
                Component.onCompleted: locked = Session.lock()
            }
            """);

        Assert.Equal(1, windows.Calls);
        Assert.True((bool)root.ReadProperty("locked")!);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Lock_reports_when_windows_refuses()
    {
        var windows = new FakeWorkstationLock(accepts: false);

        var root = Load(windows, """
            import QtQuick
            import Winshell
            Item {
                property bool locked: true
                Component.onCompleted: locked = Session.lock()
            }
            """);

        Assert.Equal(1, windows.Calls);
        Assert.False((bool)root.ReadProperty("locked")!);
        Assert.Contains("session: Windows did not lock the session.", _warnings);
    }

    [Fact]
    public void Lock_without_the_windows_desktop_does_nothing()
    {
        var root = Load(null, """
            import QtQuick
            import Winshell
            Item {
                property bool locked: true
                Component.onCompleted: locked = Session.lock()
            }
            """);

        Assert.False((bool)root.ReadProperty("locked")!);
        Assert.Single(_warnings);
    }

    [Theory]
    [InlineData("SessionLock")]
    [InlineData("SessionLockSurface")]
    [InlineData("SignInContext")]
    public void The_lock_screen_and_password_types_are_gone(string type)
    {
        var error = Assert.Throws<QmlCompileException>(() => Load(new FakeWorkstationLock(accepts: true), $$"""
            import QtQuick
            import Winshell
            Item {
                {{type}} {}
            }
            """));
        Assert.Contains(type, error.Message);
    }

    private QObject Load(IWorkstationLock? windows, string qml)
    {
        var path = Path.Combine(_directory, "shell.qml");
        File.WriteAllText(path, qml);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        var log = new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) _warnings.Add($"{category}: {message}"); } };
        var loop = new ManualEventLoop();
        var engine = new QmlEngine(registry, loop, log, _directory);
        if (windows is not null)
            engine.AddService(windows);
        var root = engine.CreateRoot(path);
        loop.Process();
        return root;
    }

    private sealed class FakeWorkstationLock(bool accepts) : IWorkstationLock
    {
        public int Calls { get; private set; }

        public bool Lock()
        {
            Calls++;
            return accepts;
        }
    }
}
