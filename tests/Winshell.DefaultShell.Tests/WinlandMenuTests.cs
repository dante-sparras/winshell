using System.Text.Json.Nodes;
using Winshell.DefaultShell.Taskbar;
using Winshell.Tray;
using Winshell.Winland;

namespace Winshell.DefaultShell.Tests;

/// <summary>Winland's menu over the real Winland service and a fake pipe: no pipe is opened and no file is opened.</summary>
public sealed class WinlandMenuTests : IDisposable
{
    private const string Winland = @"C:\Program Files\Winland\winland.exe";
    private const int WinlandProcess = 4100;
    private const string Config = @"D:\work\work.conf";
    private const string LogFile = @"D:\work\state\winland.log";

    private readonly ManualEventLoop _loop = new();
    private readonly FakeWinlandLink _link = new() { ProcessId = WinlandProcess };
    private readonly FakeTrayBackend _backend = new();
    private readonly HashSet<string> _existing = [];
    private readonly List<string> _opened = [];
    private WinlandService? _winland;
    private int _services;

    public WinlandMenuTests() =>
        Menu = new WinlandMenu(() =>
        {
            _services++;
            _winland = new WinlandService(_loop, new Log { Sink = static (_, _, _) => { } }, _link);
            _loop.Process();
            return _winland;
        }, _existing.Contains, _opened.Add);

    private WinlandMenu Menu { get; }

    /// <summary>A tray whose icons are noticed as the shell's tray area notices them, so the menu follows the pipe.</summary>
    private SystemTray Tray(params (string Program, int ProcessId)[] icons)
    {
        var tray = new SystemTray(_loop, new Log { Sink = static (_, _, _) => { } }, _backend);
        _loop.Process();
        foreach (var (program, processId) in icons)
            _backend.Add(program, processId);
        foreach (var item in tray.Items)
            Menu.Notice(item);
        return tray;
    }

    private static (string Program, int ProcessId) WinlandIcon(int process = WinlandProcess) => (Winland, process);

    [Theory]
    [InlineData(LogFile, true, new[] { "Reload R", "Edit config E", "Open log L", "Quit Q" })]
    [InlineData(LogFile, false, new[] { "Reload R", "Edit config E", "Quit Q" })]
    // Winland writes no log, elevated or with debug:disable_logs: an old winland.log there is not offered.
    [InlineData(null, true, new[] { "Reload R", "Edit config E", "Quit Q" })]
    public void The_rows_are_Winlands_and_Open_log_is_there_only_while_Winland_writes_a_log_that_exists(string? log, bool exists, string[] rows)
    {
        _link.Log = log;
        if (exists)
            _existing.Add(LogFile);
        using var tray = Tray(WinlandIcon());

        Assert.Equal(rows, Menu.Rows().Select(row => $"{row.Label} {row.AccessKey}"));
        Assert.All(Menu.Rows(), row => Assert.True(row.Enabled));
    }

    [Fact]
    public void Reload_and_Quit_go_through_the_pipe()
    {
        using var tray = Tray(WinlandIcon());

        Menu.Choose("reload");
        Menu.Choose("quit");
        _loop.Process();

        Assert.Equal(["reload", "dispatch exit"], _link.Commands);
        Assert.Empty(_opened);
    }

    [Fact]
    public void Edit_config_and_Open_log_open_the_files_Winland_reports()
    {
        _link.Log = LogFile;
        using var tray = Tray(WinlandIcon());

        Menu.Choose("editConfig");
        Menu.Choose("openLog");

        Assert.Equal([Config, LogFile], _opened);
        Assert.Empty(_link.Commands);
    }

    [Fact]
    public void Only_the_icon_of_the_process_on_Winlands_pipe_is_Winlands()
    {
        // Two Winlands from one program, each on its own pipe: only the one this shell follows is drawn here.
        using var tray = Tray(
            WinlandIcon(),
            WinlandIcon(WinlandProcess + 1),
            (@"C:\Program Files\Other\other.exe", 900),
            ("", 0));

        Assert.Equal([true, false, false, false], tray.Items.Select(Menu.Owns));

        // No Winland answers: the click goes to the app, even for an icon whose process could not be read.
        _link.ProcessId = 0;
        Assert.All(tray.Items, item => Assert.False(Menu.Owns(item)));
    }

    [Fact]
    public void A_Winland_that_does_not_answer_files_draws_its_own_menu()
    {
        _link.AnswersFiles = false;
        using var tray = Tray(WinlandIcon());

        Assert.Contains("j/files", _link.Sent);
        Assert.False(Menu.Owns(tray.Items[0]));
    }

    [Fact]
    public void Winlands_pipe_is_followed_only_once_an_icon_of_winland_exe_appears()
    {
        using var tray = Tray(
            (@"C:\Program Files\Other\other.exe", WinlandProcess),
            ("", 0));

        Assert.All(tray.Items, item => Assert.False(Menu.Owns(item)));
        Assert.Equal(0, _services);

        _backend.Add(Winland, WinlandProcess);
        Menu.Notice(tray.Items[2]);
        Menu.Notice(tray.Items[2]);

        Assert.Equal(1, _services);
        Assert.True(Menu.Owns(tray.Items[2]));
    }

    public void Dispose() => _winland?.Dispose();

    private sealed class FakeWinlandLink : IWinlandBackend
    {
        public int ProcessId { get; set; }

        /// <summary>The log Winland reports writing, or null when it writes none.</summary>
        public string? Log { get; set; }

        /// <summary>False for a Winland from before <c>files</c>, which answers it as an unknown request.</summary>
        public bool AnswersFiles { get; set; } = true;

        public List<string> Sent { get; } = [];

        /// <summary>What was sent other than the queries the service makes as it connects.</summary>
        public IEnumerable<string> Commands => Sent.Where(command => !command.StartsWith("j/", StringComparison.Ordinal));

        public bool IsConnected => ProcessId > 0;

        public string PipePath => @"\\.\pipe\winland-test";

#pragma warning disable CS0067 // The service subscribes. These tests raise no event.
        public event Action<bool>? ConnectionChanged;

        public event Action<string>? EventReceived;
#pragma warning restore CS0067

        public void Start(Log log)
        {
        }

        public Task<string?> RequestAsync(string command)
        {
            Sent.Add(command);
            return Task.FromResult<string?>(command switch
            {
                "j/files" when AnswersFiles => new JsonObject { ["config"] = Config, ["log"] = Log }.ToJsonString(),
                "j/files" => "error: unknown request",
                _ when command.StartsWith("j/", StringComparison.Ordinal) => "[]",
                _ => "ok",
            });
        }

        public void Stop(Log log)
        {
        }
    }
}
