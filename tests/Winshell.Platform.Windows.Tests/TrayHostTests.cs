using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Native;
using Winshell.Platform.Windows.Services.Tray;
using Winshell.Runtime;
using Xunit.Abstractions;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Runs notification area hosts under private window classes and a private owner name, so the real taskbar and the
/// apps in the session never see them. An "Explorer" here is another host, which records what it is passed.
/// Nothing is broadcast to the session.
/// </summary>
public sealed class TrayHostTests : IDisposable
{
    private const uint WM_COPYDATA = 0x004A;
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly ManualEventLoop _loop = new();
    private readonly Log _log;
    private readonly List<TrayHost> _hosts = [];
    private readonly nint _app;

    public TrayHostTests(ITestOutputHelper output)
    {
        _log = new Log { Sink = (_, category, message) => output.WriteLine($"{category}: {message}") };
        // The window an app would add its icon for. Message-only, so nothing else can find it.
        _app = Win32.CreateWindowExW(0, "STATIC", "", 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, 0, 0);
        Assert.NotEqual(0, _app);
    }

    private string ExplorerClass => $"WinshellTestExplorer-{_id}";

    private TrayNames HostNames => new($"WinshellTestTray-{_id}", ExplorerClass, $@"Local\winshell-test-tray-{_id}");

    [Fact]
    public void Apps_reach_the_host_and_explorer_still_gets_every_message()
    {
        var explorer = StartExplorer();
        var host = Start(HostNames, out _);
        Assert.NotEqual(0, host.Window);

        Send(host.Window, TrayCall.Build(TrayProtocol.Add, (int)_app, 1, TrayProtocol.NIF_TIP, tip: "Backup"));
        Assert.Equal("Backup", Assert.Single(host.Icons).Tip);
        Assert.Equal("Backup", Assert.Single(explorer.Icons).Tip);

        // Not an icon: an app bar message. Explorer answers it.
        Send(host.Window, new byte[16], data: 0);

        Send(host.Window, TrayCall.Build(TrayProtocol.Delete, (int)_app, 1, 0));
        Assert.Empty(host.Icons);
        Assert.Empty(explorer.Icons);
    }

    [Fact]
    public void A_restarted_explorer_is_answered_once()
    {
        var explorer = StartExplorer();
        var host = Start(HostNames, out var broadcasts);
        Assert.Equal(1, broadcasts());

        // Its own broadcast comes back to it, and Explorer's taskbar is the one it already answered.
        Win32.SendMessageW(host.Window, TaskbarCreated, 0, 0);
        _loop.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, broadcasts());

        // Explorer ends, and a new one makes a new taskbar and asks apps to add their icons.
        Stop(explorer);
        _loop.Advance(TimeSpan.FromSeconds(1));
        explorer = StartExplorer();
        Win32.SendMessageW(host.Window, TaskbarCreated, 0, 0);
        Assert.Equal(1, broadcasts());
        _loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, broadcasts());

        // That answer comes back too, and is not answered.
        Win32.SendMessageW(host.Window, TaskbarCreated, 0, 0);
        _loop.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, broadcasts());

        // Messages reach the new Explorer.
        Send(host.Window, TrayCall.Build(TrayProtocol.Add, (int)_app, 1, TrayProtocol.NIF_TIP, tip: "Again"));
        Assert.Equal("Again", Assert.Single(explorer.Icons).Tip);
    }

    [Fact]
    public void The_next_instance_takes_over_when_the_owner_exits()
    {
        var names = HostNames;
        var owner = new OtherInstance(names, _log);
        var next = Start(names, out var broadcasts);
        Assert.Equal(0, next.Window);
        _loop.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, next.Window);
        Assert.Equal(0, broadcasts());

        var window = owner.Window;
        Assert.Equal(1, owner.Broadcasts);
        owner.Exit(clean: true);
        // The owner's window went first, then it asked apps to register with whoever is first now.
        Assert.False(IsWindow(window));
        Assert.Equal(2, owner.Broadcasts);

        _loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.NotEqual(0, next.Window);
        Assert.Equal(1, broadcasts());
    }

    [Fact]
    public void The_next_instance_takes_over_when_the_owner_crashes()
    {
        var names = HostNames;
        var owner = new OtherInstance(names, _log);
        var next = Start(names, out var broadcasts);
        Assert.Equal(0, next.Window);

        owner.Exit(clean: false);
        Assert.Equal(1, owner.Broadcasts);

        _loop.Advance(TimeSpan.FromMilliseconds(500));
        Assert.NotEqual(0, next.Window);
        Assert.Equal(1, broadcasts());
    }

    [Fact]
    public void App_bar_requests_of_this_process_find_Explorers_taskbar_first_and_the_host_then_returns_to_the_front()
    {
        var explorer = StartExplorer();
        var host = Start(HostNames, out _);
        Assert.True(Above(host.Window, explorer.Window));

        using (TrayHost.ExplorerFirst())
            Assert.True(Above(explorer.Window, host.Window));

        Assert.True(Above(host.Window, explorer.Window));
    }

    [Fact]
    public void A_system_message_is_never_an_icons_callback()
    {
        var host = Start(HostNames, out _);
        Send(host.Window, TrayCall.Build(TrayProtocol.Add, (int)_app, 1, TrayProtocol.NIF_MESSAGE, callback: 0x8001));
        var icon = Assert.Single(host.Icons);
        Assert.Equal(0x8001u, icon.CallbackMessage);

        // WM_CLOSE: any app could name it for any window, and a click would close that window.
        Send(host.Window, TrayCall.Build(TrayProtocol.Modify, (int)_app, 1, TrayProtocol.NIF_MESSAGE, callback: 0x0010));
        Assert.Equal(0u, icon.CallbackMessage);
        host.Click(icon, TrayClick.Left, null);
        Assert.True(IsWindow(_app));
    }

    [Fact]
    public void An_elevated_shell_clicks_no_app_above_medium_integrity()
    {
        const uint low = 0x1000, high = 0x3000, system = 0x4000, unknown = uint.MaxValue;
        Assert.False(TrayHost.MayClick(high, high));
        Assert.False(TrayHost.MayClick(high, system));
        Assert.False(TrayHost.MayClick(high, unknown));
        Assert.True(TrayHost.MayClick(high, TrayHost.MediumIntegrity));
        Assert.True(TrayHost.MayClick(high, low));
        // Unelevated, Windows keeps the shell out of elevated apps itself, as it does Explorer.
        Assert.True(TrayHost.MayClick(TrayHost.MediumIntegrity, high));
        Assert.True(TrayHost.MayClick(TrayHost.MediumIntegrity, TrayHost.MediumIntegrity));

        Assert.InRange(TrayHost.IntegrityLevel((uint)Environment.ProcessId), low, system);
        // The System Idle Process has no token to read.
        Assert.Equal(unknown, TrayHost.IntegrityLevel(0));
    }

    [Fact]
    public void A_click_reaches_the_app_only_when_this_process_may_click_it()
    {
        var host = Start(HostNames, out _);
        Send(host.Window, TrayCall.Build(TrayProtocol.Add, (int)_app, 1, TrayProtocol.NIF_MESSAGE, callback: 0x8001));
        var icon = Assert.Single(host.Icons);
        var callbacks = 0;
        WindowProc record = (hwnd, msg, wParam, lParam) =>
        {
            if (msg == 0x8001)
                callbacks++;
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        };
        var previous = SetWindowLongPtrW(_app, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(record));
        try
        {
            host.Click(icon, TrayClick.Left, null);
        }
        finally
        {
            SetWindowLongPtrW(_app, GWLP_WNDPROC, previous);
            GC.KeepAlive(record);
        }
        // The app is this test process: medium on a developer's desktop, elevated on CI.
        var own = TrayHost.IntegrityLevel((uint)Environment.ProcessId);
        Assert.Equal(TrayHost.MayClick(own, own), callbacks > 0);
    }

    private delegate nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private const int GWLP_WNDPROC = -4;

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <summary>Whether one window is in front of another, as FindWindow and SHAppBarMessage see the order.</summary>
    private static bool Above(nint upper, nint lower)
    {
        for (var hwnd = GetWindow(upper, GW_HWNDNEXT); hwnd != 0; hwnd = GetWindow(hwnd, GW_HWNDNEXT))
        {
            if (hwnd == lower)
                return true;
        }
        return false;
    }

    private const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    private TrayHost StartExplorer()
    {
        var names = new TrayNames(ExplorerClass, $"WinshellTestNone-{_id}", $@"Local\winshell-test-explorer-{Guid.NewGuid():N}");
        return Start(names, out _);
    }

    private TrayHost Start(TrayNames names, out Func<int> broadcasts)
    {
        var count = 0;
        var host = new TrayHost(_loop, _log, names, () => count++);
        _hosts.Add(host);
        host.Start();
        broadcasts = () => count;
        return host;
    }

    private void Stop(TrayHost host)
    {
        _hosts.Remove(host);
        host.Dispose();
    }

    private static nint Send(nint window, byte[] payload, nuint data = TrayProtocol.NotifyIconData)
    {
        var bytes = Marshal.AllocHGlobal(payload.Length);
        var copy = Marshal.AllocHGlobal(IntPtr.Size * 3);
        try
        {
            Marshal.Copy(payload, 0, bytes, payload.Length);
            Marshal.WriteIntPtr(copy, 0, (nint)data);
            Marshal.WriteInt32(copy, IntPtr.Size, payload.Length);
            Marshal.WriteIntPtr(copy, IntPtr.Size * 2, bytes);
            return Win32.SendMessageW(window, WM_COPYDATA, 0, copy);
        }
        finally
        {
            Marshal.FreeHGlobal(copy);
            Marshal.FreeHGlobal(bytes);
        }
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose();
        Win32.DestroyWindow(_app);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    /// <summary>A host on its own thread, the way another winshell process holds the tray.</summary>
    private sealed class OtherInstance
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new();
        private readonly ManualResetEventSlim _exit = new();
        private bool _clean;
        private int _broadcasts;

        public OtherInstance(TrayNames names, Log log)
        {
            _thread = new Thread(() =>
            {
                var host = new TrayHost(new ManualEventLoop(), log, names, () => _broadcasts++);
                host.Start();
                Window = host.Window;
                _started.Set();
                _exit.Wait();
                // A crash ends the thread with the owner name still held; Windows hands it on as abandoned.
                if (_clean)
                    host.Dispose();
            });
            _thread.Start();
            _started.Wait();
            Assert.NotEqual(0, Window);
        }

        public nint Window { get; private set; }

        public int Broadcasts => _broadcasts;

        public void Exit(bool clean)
        {
            _clean = clean;
            _exit.Set();
            _thread.Join();
        }
    }
}
