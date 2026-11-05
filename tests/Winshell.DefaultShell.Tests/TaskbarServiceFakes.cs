using Winshell.Audio;
using Winshell.DefaultShell.Taskbar;
using Winshell.Network;
using Winshell.Power;
using Winshell.Tray;

namespace Winshell.DefaultShell.Tests;

/// <summary>A snapshot source a test feeds by hand. It never reads Windows.</summary>
internal sealed class FakeSource<T>(T initial) : SnapshotSource<T>(initial)
    where T : class
{
    protected override string Category => "test";

    public void Push(T next) => Publish(next);

    protected override Task StartAsync() => Task.CompletedTask;

    protected override Task<T> ReadAsync() => Task.FromResult(Current);
}

/// <summary>Adapters a test describes. Nothing here opens a socket or a radio.</summary>
internal sealed class FakeNetwork : INetworkBackend
{
    private Action<NetworkSnapshot>? _listener;

    public NetworkSnapshot Current { get; private set; } = NetworkSnapshot.Initial;

    public IDisposable Subscribe(Action<NetworkSnapshot> listener)
    {
        _listener = listener;
        return new Subscription(() => _listener = null);
    }

    public void Publish(NetworkSnapshot snapshot)
    {
        Current = snapshot;
        _listener?.Invoke(snapshot);
    }

    public void Refresh()
    {
    }

    public Task RequestAccessAsync() => Task.CompletedTask;

    public Task<string?> SetWifiEnabledAsync(bool enabled) => Task.FromResult<string?>(null);

    public Task ScanAsync(Guid device) => Task.CompletedTask;

    public Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? password) => Task.FromResult<ConnectionFailReason?>(null);

    public bool Disconnect(Guid device) => false;

    public uint Forget(Guid device, string profileName) => 0;

    public Task<string?> SetProfileAutoConnectAsync(Guid device, string profileName, bool autoConnect) => Task.FromResult<string?>(null);

    public Task<string?> ClearProfilePasswordAsync(Guid device, string profileName) => Task.FromResult<string?>(null);

    public Task<string?> ConnectProfileAsync(Guid device, string profileName, bool hidden) => Task.FromResult<string?>(null);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>A playback graph a test changes by hand.</summary>
internal sealed class FakeAudioGraph : IAudioGraph
{
    private readonly List<AudioGraphNode> _nodes = [];

    public IReadOnlyList<AudioGraphNode> Nodes => _nodes;

    public bool Ready => true;

    public AudioGraphNode? DefaultPlayback { get; private set; }

    public AudioGraphNode? DefaultRecording { get; private set; }

    public event Action<AudioGraphNode>? NodeAdded;

    public event Action? DefaultsChanged;

#pragma warning disable CS0067 // AudioService subscribes. These tests do not remove a node or add a link.
    public event Action<AudioGraphNode>? NodeRemoved;

#pragma warning restore CS0067

    public void SetDefault(AudioGraphNode node)
    {
    }

    public FakePlayback Add(FakePlayback node)
    {
        _nodes.Add(node);
        NodeAdded?.Invoke(node);
        return node;
    }

    public void Use(FakePlayback? node)
    {
        DefaultPlayback = node;
        DefaultsChanged?.Invoke();
    }

    public FakePlayback AddRecording(string description) => Add(new FakePlayback(description, recording: true));

    public void UseRecording(FakePlayback? node)
    {
        DefaultRecording = node;
        DefaultsChanged?.Invoke();
    }
}

internal sealed class FakePlayback : AudioGraphNode
{
    private static int _next = 1;

    public FakePlayback(string description, bool recording = false)
        : base(_next++)
    {
        Type = recording ? AudioNodeType.RecordingDevice : AudioNodeType.PlaybackDevice;
        Description = description;
        Name = description;
        Channels = [AudioChannel.FrontLeft];
        ApplyAudio([0f], false);
    }

    public override IAudioMeterInformation? Meter => null;

    public override void SetVolumes(IReadOnlyList<float> volumes) => ApplyAudio([.. volumes], Muted);

    public override void SetMuted(bool muted) => ApplyAudio([.. Volumes], muted);

    public void SetAudio(float volume, bool muted) => ApplyAudio([volume], muted);
}

/// <summary>An app's icon in <see cref="FakeTrayBackend"/>: the window it was added for, and the GUID that names it.</summary>
internal sealed record FakeTrayIcon(nint Window, Guid Guid);

/// <summary>
/// The notification area without the session mutex or the Shell_TrayWnd window. A test plays the apps that add icons
/// and Windows' choices for which icons the taskbar shows.
/// </summary>
internal sealed class FakeTrayBackend : ITrayBackend
{
    private readonly Dictionary<nint, (int ProcessId, string Program)> _windows = [];
    private readonly List<TrayIconSetting> _settings = [];
    private Func<NotifyIconCall, bool>? _apply;
    private nint _nextWindow = 0x100;

#pragma warning disable CS0067 // SystemTray subscribes. These tests play no Explorer.
    public event Action? TaskbarCreated;

    public event Action? ExplorerInFront;
#pragma warning restore CS0067

    public event Action? SettingsChanged;

    public nint Explorer => 0;

    public (int X, int Y) Cursor => (0, 0);

    /// <summary>An app adds an icon for a window of its own, by GUID, and Windows shows it on the bar or in the flyout.</summary>
    /// <param name="program">The program behind the window, or empty when it cannot be read.</param>
    public FakeTrayIcon Add(string program = "", int processId = 0, string tip = "", bool onBar = false, bool hidden = false)
    {
        var icon = new FakeTrayIcon(_nextWindow++, Guid.NewGuid());
        _windows[icon.Window] = (processId, program);
        if (onBar)
            Move(icon, onBar);
        Send(new NotifyIconCall
        {
            Message = TrayProtocol.Add,
            Window = icon.Window,
            Id = 1,
            Guid = icon.Guid,
            Flags = TrayProtocol.NIF_GUID | TrayProtocol.NIF_TIP | TrayProtocol.NIF_STATE,
            Tip = tip,
            State = hidden ? TrayProtocol.NIS_HIDDEN : 0,
            StateMask = TrayProtocol.NIS_HIDDEN,
        });
        return icon;
    }

    /// <summary>The user moves an icon onto the bar or into the hidden icons flyout in Settings.</summary>
    public void Move(FakeTrayIcon icon, bool onBar)
    {
        _settings.RemoveAll(setting => setting.Guid == icon.Guid);
        _settings.Add(new TrayIconSetting(icon.Guid, "", null, onBar));
        SettingsChanged?.Invoke();
    }

    public void Remove(FakeTrayIcon icon) =>
        Send(new NotifyIconCall { Message = TrayProtocol.Delete, Window = icon.Window, Id = 1, Guid = icon.Guid, Flags = TrayProtocol.NIF_GUID });

    private void Send(NotifyIconCall call) => (_apply ?? throw new InvalidOperationException("The tray has not opened its window."))(call);

    public bool TryOwn() => true;

    public bool Open(Func<NotifyIconCall, bool> apply)
    {
        _apply = apply;
        return true;
    }

    public void Close() => _apply = null;

    public void Dispose()
    {
    }

    public void Raise()
    {
    }

    public bool Lower() => false;

    public void Broadcast()
    {
    }

    public IReadOnlyList<TrayIconSetting> ReadSettings() => [.. _settings];

    public (PixelImage? Image, IconResource? Resource) ReadIcon(nint icon) => (null, null);

    public uint ProcessOf(nint window) => (uint)_windows.GetValueOrDefault(window).ProcessId;

    public string ProgramOf(nint window) => _windows.GetValueOrDefault(window).Program ?? "";

    public uint? IntegrityOf(uint processId) => null;

    public void MoveCursor((int X, int Y) point)
    {
    }

    public void LetAppTakeForeground(nint window, uint processId)
    {
    }

    public void Notify(nint window, uint message, nint wParam, nint lParam)
    {
    }
}

/// <summary>The four services the status icons follow, fed by the fakes above.</summary>
internal sealed class StatusRig : IDisposable
{
    public StatusRig()
    {
        Loop = new ManualEventLoop();
        Log = new Log { Sink = static (_, _, _) => { } };
        Network = new FakeNetwork();
        Audio = new FakeAudioGraph();
        Power = new FakeSource<PowerSnapshot>(PowerSnapshot.Initial);
        Modes = new FakeSource<PowerModeSnapshot>(PowerModeSnapshot.Initial);
        Networking = new NetworkingService(Loop, Network);
        AudioService = new AudioService(Audio, Log);
        PowerService = new PowerService(Loop, Power);
        PowerMode = new PowerModeService(Loop, Log, Modes, static _ => 0);
        Status = new SystemStatus(Networking, AudioService, PowerService, PowerMode);
    }

    public ManualEventLoop Loop { get; }

    public Log Log { get; }

    public FakeNetwork Network { get; }

    public FakeAudioGraph Audio { get; }

    public FakeSource<PowerSnapshot> Power { get; }

    public FakeSource<PowerModeSnapshot> Modes { get; }

    public NetworkingService Networking { get; }

    public AudioService AudioService { get; }

    public PowerService PowerService { get; }

    public PowerModeService PowerMode { get; }

    public SystemStatus Status { get; }

    public void Show(NetworkSnapshot snapshot)
    {
        Network.Publish(snapshot);
        Loop.Process();
    }

    public void Dispose()
    {
        Networking.Dispose();
        AudioService.Dispose();
        PowerService.Dispose();
        PowerMode.Dispose();
    }
}
