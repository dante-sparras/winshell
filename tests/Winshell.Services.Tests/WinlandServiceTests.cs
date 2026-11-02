using System.ComponentModel;
using Winshell.Winland;

namespace Winshell.Services.Tests;

/// <summary>The Winland service over a fake link: no pipe is opened, and replies come from a dictionary.</summary>
public sealed class WinlandServiceTests
{
    private const string Monitors = """
        [{"id":1,"name":"\\\\.\\DISPLAY2","description":"Left","x":-1920,"y":0,"width":1920,"height":1080,"scale":1.5,"focused":false,
          "activeWorkspace":{"id":3,"name":"3"}},
         {"id":0,"name":"\\\\.\\DISPLAY1","description":"Main","x":0,"y":0,"width":2560,"height":1440,"scale":1,"focused":true,
          "activeWorkspace":{"id":1,"name":"1"}}]
        """;

    private const string Workspaces = """
        [{"id":3,"name":"3","monitor":"\\\\.\\DISPLAY1","monitorID":0,"hasfullscreen":false},
         {"id":1,"name":"1","monitor":"\\\\.\\DISPLAY1","monitorID":0,"hasfullscreen":true},
         {"id":3,"name":"3","monitor":"\\\\.\\DISPLAY2","monitorID":1,"hasfullscreen":false},
         {"name":"notes","monitor":"\\\\.\\DISPLAY1","monitorID":0},
         {"name":"special:special"}]
        """;

    private const string Clients = """
        [{"address":"0x10","title":"Editor","workspace":{"id":1,"name":"1","monitor":"\\\\.\\DISPLAY1"}},
         {"address":"0x20","title":"Browser","workspace":{"id":3,"name":"3","monitor":"\\\\.\\DISPLAY2"}}]
        """;

    private readonly ManualEventLoop _loop = new();
    private readonly FakeLink _link = new();

    private WinlandService Connected()
    {
        _link.Replies["j/monitors"] = Monitors;
        _link.Replies["j/workspaces"] = Workspaces;
        _link.Replies["j/clients"] = Clients;
        _link.Replies["j/activewindow"] = """{"address":"0x10"}""";
        _link.Connected = true;
        var service = new WinlandService(_loop, Log.Default, _link);
        _loop.Process();
        return service;
    }

    private void Raise(string line)
    {
        _link.Raise(line);
        _loop.Process();
    }

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void A_connection_fills_the_lists_in_order()
    {
        using var service = Connected();

        Assert.Equal([0, 1], service.Monitors.Select(m => m.Id));
        Assert.Equal(["special:special", "notes", "1", "3", "3"], service.Workspaces.Select(w => w.Name));
        Assert.Equal(["10", "20"], service.Toplevels.Select(t => t.Address));
        Assert.Equal((nint)0x10, service.Toplevels[0].Handle);
        Assert.Equal(-99, service.Workspaces.Single(w => w.Name == "special:special").Id);
        Assert.Equal(-1, service.Workspaces.Single(w => w.Name == "notes").Id);

        var main = service.FocusedMonitor!;
        Assert.Equal(@"\\.\DISPLAY1", main.Name);
        Assert.Equal("Main", main.Description);
        Assert.Equal(2560, main.Width);
        Assert.True(main.Focused);
        Assert.Equal("1", main.ActiveWorkspace!.Name);
        Assert.Equal("Main", main.LastIpcObject!.Value.GetProperty("description").GetString());

        var left = service.Monitors[1];
        Assert.Equal((-1920, 0, 1.5), (left.X, left.Y, left.Scale));
        Assert.False(left.Focused);

        var focused = service.FocusedWorkspace!;
        Assert.Same(main.ActiveWorkspace, focused);
        Assert.True(focused.Focused);
        Assert.True(focused.Active);
        Assert.True(focused.HasFullscreen);
        Assert.Equal("1", focused.Address);

        Assert.Same(service.Toplevels[0], service.ActiveToplevel);
        Assert.True(service.ActiveToplevel!.Activated);
        Assert.Equal("Editor", service.ActiveToplevel.Title);
        Assert.Same(focused, service.ActiveToplevel.Workspace);
        Assert.Same(main, service.ActiveToplevel.Monitor);
        Assert.Same(service.ActiveToplevel, Assert.Single(focused.Toplevels));
    }

    [Fact]
    public void Two_workspaces_with_one_number_stay_apart_by_monitor()
    {
        using var service = Connected();

        var threes = service.Workspaces.Where(w => w.Name == "3").ToArray();

        Assert.Equal(2, threes.Length);
        Assert.Equal([@"\\.\DISPLAY1", @"\\.\DISPLAY2"], threes.Select(w => w.Monitor!.Name));
        Assert.False(threes[0].Active);
        Assert.True(threes[1].Active);
        Assert.Equal("20", Assert.Single(threes[1].Toplevels).Address);
        Assert.Empty(threes[0].Toplevels);
    }

    [Fact]
    public void An_event_updates_the_same_objects_then_is_raised()
    {
        using var service = Connected();
        var window = service.Toplevels[0];
        var changes = Watch(window);
        var seen = new List<(string Name, string Title)>();
        service.RawEvent += e => seen.Add((e.Name, window.Title));

        Raise("windowtitlev2>>10,New, title");

        Assert.Equal("New, title", window.Title);
        Assert.Equal([nameof(WinlandWindow.Title)], changes);
        Assert.Equal(("windowtitlev2", "New, title"), Assert.Single(seen));
        Assert.Same(window, service.Toplevels[0]);
    }

    [Fact]
    public void An_event_splits_into_its_arguments()
    {
        var parsed = new WinlandEvent("openwindow", "10,1,a,b,c").Parse(3);

        Assert.Equal(["10", "1", "a,b,c"], parsed);
        Assert.Equal(["x", "", ""], new WinlandEvent("e", "x").Parse(3));
    }

    [Fact]
    public void Showing_a_workspace_moves_active_and_focus()
    {
        using var service = Connected();
        var main = service.FocusedMonitor!;
        var old = service.FocusedWorkspace!;
        var three = service.Workspaces.First(w => w.Name == "3" && w.Monitor == main);

        Raise(@"workspacev2>>3,3,\\.\DISPLAY1");

        Assert.Same(three, service.FocusedWorkspace);
        Assert.Same(three, main.ActiveWorkspace);
        Assert.True(three.Active);
        Assert.True(three.Focused);
        Assert.False(old.Active);
        Assert.False(old.Focused);
    }

    [Fact]
    public void Focus_moves_between_monitors()
    {
        using var service = Connected();
        var left = service.Monitors[1];

        Raise(@"focusedmon>>\\.\DISPLAY2,3");

        Assert.Same(left, service.FocusedMonitor);
        Assert.True(left.Focused);
        Assert.False(service.Monitors[0].Focused);
        Assert.Same(left.ActiveWorkspace, service.FocusedWorkspace);
    }

    [Fact]
    public void Windows_open_move_and_close()
    {
        using var service = Connected();
        var main = service.FocusedWorkspace!;
        var three = service.Workspaces.First(w => w.Name == "3" && w.Monitor!.Name == @"\\.\DISPLAY1");

        Raise(@"openwindow>>30,1,\\.\DISPLAY1,App,Title, with comma");
        var window = service.Toplevels.Single(t => t.Address == "30");
        Assert.Equal("Title, with comma", window.Title);
        Assert.Contains(window, main.Toplevels);

        Raise(@"movewindowv2>>30,3,3,\\.\DISPLAY1");
        Assert.Same(three, window.Workspace);
        Assert.Contains(window, three.Toplevels);
        Assert.DoesNotContain(window, main.Toplevels);

        Raise("activewindowv2>>30");
        Assert.Same(window, service.ActiveToplevel);
        Assert.False(service.Toplevels[0].Activated);

        Raise("closewindow>>30");
        Assert.DoesNotContain(window, service.Toplevels);
        Assert.Empty(three.Toplevels);
        Assert.Null(service.ActiveToplevel);
    }

    [Fact]
    public void An_urgent_window_marks_its_workspace_until_it_is_focused()
    {
        using var service = Connected();
        var other = service.Toplevels[1];
        var workspace = other.Workspace!;

        Raise("urgent>>20");
        Assert.True(other.Urgent);
        Assert.True(workspace.Urgent);

        Raise(@"focusedmon>>\\.\DISPLAY2,3");
        Assert.False(other.Urgent);
        Assert.False(workspace.Urgent);
    }

    [Fact]
    public void A_workspace_comes_and_goes()
    {
        using var service = Connected();

        Raise(@"createworkspacev2>>7,7,\\.\DISPLAY1");
        var seven = service.Workspaces.Single(w => w.Name == "7");
        Assert.Equal(7, seven.Id);

        Raise(@"destroyworkspacev2>>7,7,\\.\DISPLAY1");
        Assert.DoesNotContain(seven, service.Workspaces);
    }

    [Fact]
    public void A_monitor_that_goes_leaves_its_workspaces_with_none()
    {
        using var service = Connected();
        var left = service.Monitors[1];
        var three = left.ActiveWorkspace!;

        Raise(@"monitorremoved>>\\.\DISPLAY2");

        Assert.DoesNotContain(left, service.Monitors);
        Assert.Null(three.Monitor);
    }

    [Fact]
    public void A_lost_connection_empties_everything_and_a_late_reply_is_dropped()
    {
        using var service = Connected();
        var late = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _link.Pending = late;
        service.RefreshToplevels();

        _link.RaiseConnection(false);
        _loop.Process();

        Assert.Empty(service.Monitors);
        Assert.Empty(service.Workspaces);
        Assert.Empty(service.Toplevels);
        Assert.Null(service.FocusedMonitor);
        Assert.Null(service.FocusedWorkspace);
        Assert.Null(service.ActiveToplevel);

        _link.Pending = null;
        late.SetResult(Clients);
        Wait.ForNothingToHappen(TimeSpan.FromMilliseconds(50));
        _loop.Process();
        Assert.Empty(service.Toplevels);

        _link.Connected = true;
        _link.RaiseConnection(true);
        _loop.Process();
        Assert.Equal(2, service.Monitors.Count);
    }

    [Fact]
    public void A_query_with_no_answer_is_asked_again_while_the_stream_is_up()
    {
        _link.Connected = true;
        using var service = new WinlandService(_loop, Log.Default, _link);
        _loop.Process();
        Assert.Empty(service.Monitors);

        _link.Replies["j/monitors"] = Monitors;
        _loop.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, service.Monitors.Count);
    }

    [Fact]
    public void A_screen_finds_its_monitor_and_before_the_first_list_one_is_added()
    {
        var screen = new ScreenInfo(@"\\.\DISPLAY1", "DISPLAY1", "", "", "", default, default, 1, 0, 0, 60, true);
        using (var early = new WinlandService(_loop, Log.Default, _link))
        {
            var added = early.MonitorFor(screen);
            Assert.Equal(@"\\.\DISPLAY1", added!.Name);
            Assert.Null(early.MonitorFor(null));
        }

        using var service = Connected();
        Assert.Same(service.Monitors[0], service.MonitorFor(screen));
        Assert.Null(service.MonitorFor(screen with { Id = "other", Name = "other" }));
    }

    [Fact]
    public void Dispatch_sends_the_request_and_a_refusal_is_logged()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (_, _, message) => lines.Add(message) };
        using var service = new WinlandService(_loop, log, _link);
        _link.Replies["dispatch workspace 3"] = "ok";
        _link.Replies["dispatch bad"] = "unknown request";

        service.Dispatch("workspace 3");
        service.Dispatch("bad");
        service.Dispatch("nothing");
        _loop.Process();

        Assert.Equal(["dispatch workspace 3", "dispatch bad", "dispatch nothing"], _link.Sent);
        Assert.Equal(2, lines.Count);
        Assert.Contains("unknown request", lines[0], StringComparison.Ordinal);
        Assert.Contains("not running", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Activating_a_workspace_on_another_monitor_focuses_that_monitor_first()
    {
        using var service = Connected();
        _link.Sent.Clear();

        service.Workspaces.First(w => w.Name == "3" && w.Monitor!.Id == 1).Activate();
        service.Workspaces.First(w => w.Name == "3" && w.Monitor!.Id == 0).Activate();
        service.Workspaces.First(w => w.Name == "special:special").Activate();

        Assert.Equal(
        [
            "[[BATCH]]dispatch focusmonitor 1;dispatch workspace 3",
            "dispatch workspace 3",
            "dispatch workspace special:special",
        ], _link.Sent);
    }

    [Fact]
    public void The_pipe_path_and_lua_are_what_Winland_says()
    {
        using var service = new WinlandService(_loop, Log.Default, _link);

        Assert.Equal(@"\\.\pipe\winland-test", service.RequestSocketPath);
        Assert.Equal(service.RequestSocketPath, service.EventSocketPath);
        Assert.False(service.UsingLua);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_stream()
    {
        var service = Connected();
        var title = service.Toplevels[0].Title;

        _link.Raise("windowtitlev2>>10,Late");
        service.Dispose();
        _loop.Process();

        Assert.Equal(title, service.Toplevels[0].Title);
        Assert.Equal(1, _link.Disposals);
        Assert.Equal(0, _link.Listeners);
    }

    private sealed class FakeLink : IWinlandLink
    {
        public bool Connected { get; set; }

        public Dictionary<string, string> Replies { get; } = [];

        public List<string> Sent { get; } = [];

        public TaskCompletionSource<string?>? Pending { get; set; }

        public int Disposals { get; private set; }

        public int Listeners => (ConnectionChanged?.GetInvocationList().Length ?? 0) + (EventReceived?.GetInvocationList().Length ?? 0);

        public bool IsConnected => Connected;

        public string PipePath => @"\\.\pipe\winland-test";

        public event Action<bool>? ConnectionChanged;

        public event Action<string>? EventReceived;

        public void Start()
        {
        }

        public Task<string?> RequestAsync(string command)
        {
            Sent.Add(command);
            return Pending?.Task ?? Task.FromResult(Replies.GetValueOrDefault(command));
        }

        public void Raise(string line) => EventReceived?.Invoke(line);

        public void RaiseConnection(bool connected)
        {
            Connected = connected;
            ConnectionChanged?.Invoke(connected);
        }

        public void Dispose() => Disposals++;
    }
}
