using Winshell.Native;
using Winshell.Platform.Windows.Services.Tray;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The <c>Winshell.Tray</c> API over a host that is never started, fed the calls apps make. Nothing here touches the
/// real notification area; the overflow settings test only reads the registry.
/// </summary>
[Collection(ThemeCollection.Name)]
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
    public void An_icon_from_a_resource_is_drawn_again_at_the_size_asked_for()
    {
        // A 16-pixel icon from shell32's resources, as an app loads it at 100%, and a copy that no longer knows its resource.
        var shell32 = LoadLibraryExW(Path.Combine(Environment.SystemDirectory, "shell32.dll"), 0, 0x2 | 0x20);
        var loaded = LoadImageW(shell32, 4, 1, 16, 16, 0);
        var copied = CopyIcon(loaded);
        TrayIconProvider.Host = _host;
        try
        {
            Call(TrayProtocol.Add, TrayProtocol.NIF_ICON, icon: loaded, id: 1);
            Call(TrayProtocol.Add, TrayProtocol.NIF_ICON, icon: copied, id: 2);
            var fromResource = _host.Icons.Single(i => i.Id == 1);
            var drawn = _host.Icons.Single(i => i.Id == 2);
            Assert.NotNull(fromResource.Resource);
            Assert.Null(drawn.Resource);
            Assert.Equal(16, fromResource.Image?.Width);

            // An Image without a sourceSize gets the app's bitmap; with one, sourceSize times the screen's scale.
            Assert.Equal(16, Width(fromResource, 0));
            Assert.Equal(16, Width(fromResource, 16));
            Assert.Equal(24, Width(fromResource, 24));
            Assert.Equal(32, Width(fromResource, 32));
            Assert.Equal(16, Width(drawn, 32));
        }
        finally
        {
            TrayIconProvider.Host = null;
            DestroyIcon(copied);
            DestroyIcon(loaded);
            FreeLibrary(shell32);
        }

        static int? Width(TrayIcon icon, int size)
        {
            using var image = TrayIconProvider.Instance.Load($"{icon.Key}/{icon.ImageGeneration}", size, size);
            return image?.Width;
        }
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

    private void Call(uint message, uint flags, string tip = "", uint state = 0, uint stateMask = 0, string info = "", string infoTitle = "", uint infoFlags = 0, uint timeoutOrVersion = 0, nint icon = 0, uint id = 1)
    {
        var call = TrayProtocol.Read(TrayCall.Build(message, (int)_app, id, flags, icon: (int)icon, tip: tip, state: state, stateMask: stateMask, info: info,
            infoTitle: infoTitle, infoFlags: infoFlags, timeoutOrVersion: timeoutOrVersion));
        _host.OnNotifyIcon(call!);
        _loop.Process();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint LoadLibraryExW(string fileName, nint file, uint flags);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint LoadImageW(nint instance, nint name, uint type, int width, int height, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint CopyIcon(nint icon);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

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
