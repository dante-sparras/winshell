using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.Tray;
using Winshell.Runtime;
using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The <c>Winshell.Tray</c> API over a host that is never started, fed the calls apps make. Nothing here touches the
/// real notification area; the overflow settings test only reads the registry.
/// </summary>
public sealed class SystemTrayTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _directory;
    private readonly ManualEventLoop _loop = new();
    private readonly TrayHost _host;
    private readonly QmlEngine _engine;
    private readonly QObject _root;
    private readonly nint _app;
    private readonly string _program = Environment.ProcessPath!;

    public SystemTrayTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "shell.qml"), """
            import QtQuick
            import Winshell.Tray
            Item {
              property var items: SystemTray.items.values
              property var first: items.length > 0 ? items[0] : null
              property int count: items.length
              property string tooltip: first ? first.tooltip : ""
              property bool overflow: first ? first.overflow : false
              property string appName: first ? first.appName : ""
              property string executable: first ? first.executable : ""
              property string itemId: first ? first.id : ""
              property var clicks: first ? [typeof first.click, typeof first.doubleClick, typeof first.middleClick, typeof first.display, typeof first.activate, typeof first.secondaryActivate] : []
              property string balloonTitle
              property string balloonMessage
              property int balloonIcon: -1
              property int warning: BalloonIcon.Warning
              property int app: BalloonIcon.App
              Connections {
                target: first
                function onBalloon(title, message, icon) {
                  balloonTitle = title
                  balloonMessage = message
                  balloonIcon = icon
                }
              }
            }
            """);
        var log = new Log { Sink = (_, category, message) => output.WriteLine($"{category}: {message}") };
        var id = Guid.NewGuid().ToString("N");
        _host = new TrayHost(_loop, log, new TrayNames($"WinshellTestTray-{id}", $"WinshellTestNone-{id}", $@"Local\winshell-test-tray-{id}"), () => { });
        _app = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, 0, 0);
        var registry = StandardModules.Create();
        WindowsModules.Register(registry);
        ServiceModules.Register(registry);
        _engine = new QmlEngine(registry, _loop, log, _directory);
        _engine.AddService(_host);
        _root = _engine.CreateRoot(Path.Combine(_directory, "shell.qml"));
        _loop.Process();
    }

    [Fact]
    public void An_icon_shows_its_program_and_tooltip()
    {
        Call(TrayProtocol.Add, TrayProtocol.NIF_TIP | TrayProtocol.NIF_MESSAGE, tip: "Sync\nUp to date");
        Assert.Equal(1, _root.ReadProperty("count"));
        Assert.Equal("Sync\nUp to date", _root.ReadProperty("tooltip"));
        Assert.Equal(_program, _root.ReadProperty("executable"));
        Assert.Equal(Path.GetFileNameWithoutExtension(_program), _root.ReadProperty("itemId"));
        Assert.False(string.IsNullOrEmpty((string?)_root.ReadProperty("appName")));

        // Without a callback message the app asked for no clicks, so these send nothing.
        Assert.Equal(["function", "function", "function", "function", "undefined", "undefined"], ((System.Collections.IEnumerable)_root.ReadProperty("clicks")!).Cast<object>().Select(o => o.ToString()));

        Call(TrayProtocol.Modify, TrayProtocol.NIF_TIP, tip: "Syncing");
        Assert.Equal("Syncing", _root.ReadProperty("tooltip"));

        Call(TrayProtocol.Delete, 0);
        Assert.Equal(0, _root.ReadProperty("count"));
    }

    [Fact]
    public void An_icon_the_app_hides_leaves_the_list_until_it_shows_it_again()
    {
        Call(TrayProtocol.Add, TrayProtocol.NIF_TIP | TrayProtocol.NIF_STATE, tip: "Hidden", state: TrayProtocol.NIS_HIDDEN, stateMask: TrayProtocol.NIS_HIDDEN);
        Assert.Equal(0, _root.ReadProperty("count"));
        Assert.Single(_host.Icons);

        Call(TrayProtocol.Modify, TrayProtocol.NIF_STATE, state: 0, stateMask: TrayProtocol.NIS_HIDDEN);
        Assert.Equal(1, _root.ReadProperty("count"));
        Assert.Equal("Hidden", _root.ReadProperty("tooltip"));

        // A change that does not touch the hidden state leaves it as it is.
        Call(TrayProtocol.Modify, TrayProtocol.NIF_STATE, state: TrayProtocol.NIS_HIDDEN, stateMask: 0);
        Assert.Equal(1, _root.ReadProperty("count"));

        Call(TrayProtocol.Modify, TrayProtocol.NIF_STATE, state: TrayProtocol.NIS_HIDDEN, stateMask: TrayProtocol.NIS_HIDDEN);
        Assert.Equal(0, _root.ReadProperty("count"));
    }

    [Fact]
    public void Overflow_follows_the_windows_setting()
    {
        Call(TrayProtocol.Add, TrayProtocol.NIF_TIP, tip: "New");
        // Windows has no choice for a new icon yet, and puts it in the flyout.
        Assert.True((bool)_root.ReadProperty("overflow")!);

        _host.UseSettings([new TrayIconSetting(Guid.Empty, _program.ToUpperInvariant(), 1, Promoted: true)]);
        Assert.False((bool)_root.ReadProperty("overflow")!);

        _host.UseSettings([new TrayIconSetting(Guid.Empty, _program, 1, Promoted: false)]);
        Assert.True((bool)_root.ReadProperty("overflow")!);
    }

    [Fact]
    public void A_balloon_reaches_its_icon()
    {
        Call(TrayProtocol.Add, TrayProtocol.NIF_TIP, tip: "Backup");
        Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO, info: "Backup finished", infoTitle: "Backup", infoFlags: 2 | 0x10, timeoutOrVersion: 10000);
        Assert.Equal("Backup", _root.ReadProperty("balloonTitle"));
        Assert.Equal("Backup finished", _root.ReadProperty("balloonMessage"));
        Assert.Equal(_root.ReadProperty("warning"), _root.ReadProperty("balloonIcon"));

        // An empty text takes a balloon down, and is not a balloon.
        _root.WriteProperty("balloonIcon", -1);
        Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO, info: "", infoFlags: 4);
        Assert.Equal(-1, _root.ReadProperty("balloonIcon"));

        Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO, info: "Mine", infoFlags: 4);
        Assert.Equal(_root.ReadProperty("app"), _root.ReadProperty("balloonIcon"));
    }

    [Fact]
    public void Only_nim_setversion_sets_the_version()
    {
        // A balloon timeout of 4 is not interface version 4.
        Call(TrayProtocol.Add, TrayProtocol.NIF_INFO, info: "Hi", timeoutOrVersion: TrayProtocol.Version4);
        var icon = Assert.Single(_host.Icons);
        Assert.Equal(0u, icon.Version);

        Call(TrayProtocol.SetVersion, 0, timeoutOrVersion: TrayProtocol.Version4);
        Assert.Equal(TrayProtocol.Version4, icon.Version);

        Call(TrayProtocol.Modify, TrayProtocol.NIF_INFO, info: "Again", timeoutOrVersion: TrayProtocol.Version);
        Assert.Equal(TrayProtocol.Version4, icon.Version);
    }

    [Fact]
    public void Overflow_settings_match_by_guid_or_by_program_and_id()
    {
        var guid = Guid.NewGuid();
        List<TrayIconSetting> settings =
        [
            new(guid, "", null, Promoted: true),
            new(Guid.Empty, @"C:\Apps\sync.exe", 1, Promoted: true),
            new(Guid.Empty, @"C:\Apps\sync.exe", 2, Promoted: false),
        ];
        Assert.False(TrayIconSettings.InOverflow(settings, guid, @"C:\Other\app.exe", 9));
        Assert.False(TrayIconSettings.InOverflow(settings, Guid.Empty, @"c:\apps\SYNC.exe", 1));
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.Empty, @"C:\Apps\sync.exe", 2));
        // Unknown: a new icon, which Windows 11 puts in the flyout.
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.Empty, @"C:\Apps\sync.exe", 3));
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.NewGuid(), @"C:\Apps\sync.exe", 1));
        // An icon with a GUID is not matched by program, nor a program by a GUID entry.
        Assert.True(TrayIconSettings.InOverflow([new(Guid.Empty, @"C:\Apps\sync.exe", 1, true)], Guid.NewGuid(), @"C:\Apps\sync.exe", 1));
        Assert.True(TrayIconSettings.InOverflow([new(guid, @"C:\Apps\sync.exe", 1, true)], Guid.Empty, @"C:\Apps\sync.exe", 1));
    }

    [Fact]
    public void Windows_settings_read_with_known_folders_expanded()
    {
        // Read-only: the choices Explorer has stored for this user.
        var settings = TrayIconSettings.Read();
        _output.WriteLine($"{settings.Count} icon settings, {settings.Count(s => s.Promoted)} shown in the taskbar, {settings.Count(s => s.Guid != Guid.Empty)} by GUID");
        Assert.All(settings.Where(s => s.ExecutablePath.Length > 0), s => Assert.False(s.ExecutablePath.StartsWith('{'), s.ExecutablePath));
    }

    private void Call(uint message, uint flags, string tip = "", uint state = 0, uint stateMask = 0, string info = "", string infoTitle = "", uint infoFlags = 0, uint timeoutOrVersion = 0)
    {
        var call = TrayProtocol.Read(TrayCall.Build(message, (int)_app, 1, flags, tip: tip, state: state, stateMask: stateMask, info: info,
            infoTitle: infoTitle, infoFlags: infoFlags, timeoutOrVersion: timeoutOrVersion));
        _host.OnNotifyIcon(call!);
        _loop.Process();
    }

    public void Dispose()
    {
        _engine.Dispose();
        _host.Dispose();
        Win32.DestroyWindow(_app);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
