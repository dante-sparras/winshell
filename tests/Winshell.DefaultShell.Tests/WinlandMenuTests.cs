using Winshell.DefaultShell.Taskbar;
using Winshell.Tray;
using Winshell.Winland;

namespace Winshell.DefaultShell.Tests;

/// <summary>Winland's menu over the real Winland service and a fake pipe: no pipe is opened and no file is opened.</summary>
public sealed class WinlandMenuTests : IDisposable
{
    private const string Winland = @"C:\Program Files\Winland\winland.exe";

    private readonly ManualEventLoop _loop = new();
    private readonly FakeWinlandLink _link = new() { Executable = Winland };
    private readonly WinlandService _winland;
    private readonly List<string> _opened = [];
    private bool _logExists;
    private int _services;

    public WinlandMenuTests()
    {
        _winland = new WinlandService(_loop, new Log { Sink = static (_, _, _) => { } }, _link);
        Menu = new WinlandMenu(() =>
        {
            _services++;
            return _winland;
        }, path => path == _winland.LogPath && _logExists, _opened.Add);
    }

    private WinlandMenu Menu { get; }

    [Theory]
    [InlineData(false, new[] { "Reload R", "Edit config E", "Quit Q" })]
    [InlineData(true, new[] { "Reload R", "Edit config E", "Open log L", "Quit Q" })]
    public void The_rows_are_Winlands_and_Open_log_is_there_only_while_the_log_exists(bool logExists, string[] rows)
    {
        _logExists = logExists;

        Assert.Equal(rows, Menu.Rows().Select(row => $"{row.Label} {row.AccessKey}"));
        Assert.All(Menu.Rows(), row => Assert.True(row.Enabled));
    }

    [Fact]
    public void Reload_and_Quit_go_through_the_pipe()
    {
        Menu.Choose("reload");
        Menu.Choose("quit");
        _loop.Process();

        Assert.Equal(["reload", "dispatch exit"], _link.Commands);
        Assert.Empty(_opened);
    }

    [Fact]
    public void Edit_config_and_Open_log_open_Winlands_files()
    {
        Menu.Choose("editConfig");
        Menu.Choose("openLog");

        Assert.Equal([_winland.ConfigPath, _winland.LogPath], _opened);
        Assert.Empty(_link.Commands);
    }

    [Fact]
    public void Only_the_icon_of_the_program_on_Winlands_pipe_is_Winlands()
    {
        var host = new FakeTrayHost();
        using var tray = new SystemTray(_loop, new Log { Sink = static (_, _, _) => { } }, host);
        host.Add(new TrayIcon { Key = "winland", ProgramPath = Winland.ToUpperInvariant() });
        host.Add(new TrayIcon { Key = "other", ProgramPath = @"C:\Program Files\Other\other.exe" });
        host.Add(new TrayIcon { Key = "unknown" });
        var (winland, other, unknown) = (tray.Items[0], tray.Items[1], tray.Items[2]);

        Assert.True(Menu.Owns(winland));
        Assert.False(Menu.Owns(other));
        Assert.False(Menu.Owns(unknown));

        // No Winland answers: the click goes to the app, even for an icon whose program could not be read.
        _link.Executable = "";
        Assert.False(Menu.Owns(winland));
        Assert.False(Menu.Owns(unknown));
    }

    [Fact]
    public void Winlands_pipe_is_followed_only_once_an_icon_of_winland_exe_appears()
    {
        var host = new FakeTrayHost();
        using var tray = new SystemTray(_loop, new Log { Sink = static (_, _, _) => { } }, host);
        host.Add(new TrayIcon { Key = "other", ProgramPath = @"C:\Program Files\Other\other.exe" });
        host.Add(new TrayIcon { Key = "unknown" });

        foreach (var item in tray.Items)
        {
            Menu.Notice(item);
            Assert.False(Menu.Owns(item));
        }
        Assert.Equal(0, _services);

        host.Add(new TrayIcon { Key = "winland", ProgramPath = Winland });
        Menu.Notice(tray.Items[2]);
        Menu.Notice(tray.Items[2]);

        Assert.Equal(1, _services);
        Assert.True(Menu.Owns(tray.Items[2]));
    }

    public void Dispose() => _winland.Dispose();

    private sealed class FakeWinlandLink : IWinlandLink
    {
        public string Executable { get; set; } = "";

        public List<string> Sent { get; } = [];

        /// <summary>What was sent other than the queries the service makes as it connects.</summary>
        public IEnumerable<string> Commands => Sent.Where(command => !command.StartsWith("j/", StringComparison.Ordinal));

        public bool IsConnected => Executable.Length > 0;

        public string PipePath => @"\\.\pipe\winland-test";

#pragma warning disable CS0067 // The service subscribes. These tests raise no event.
        public event Action<bool>? ConnectionChanged;

        public event Action<string>? EventReceived;
#pragma warning restore CS0067

        public void Start()
        {
        }

        public Task<string?> RequestAsync(string command)
        {
            Sent.Add(command);
            return Task.FromResult<string?>(command.StartsWith("j/", StringComparison.Ordinal) ? null : "ok");
        }

        public void Dispose()
        {
        }
    }
}
