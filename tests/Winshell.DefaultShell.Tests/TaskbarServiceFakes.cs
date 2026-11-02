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

    public IReadOnlyList<AudioGraphLink> Links { get; } = [];

    public bool Ready => true;

    public AudioGraphNode? DefaultPlayback { get; private set; }

    public AudioGraphNode? DefaultRecording => null;

    public event Action<AudioGraphNode>? NodeAdded;

    public event Action? DefaultsChanged;

#pragma warning disable CS0067 // AudioService subscribes. These tests do not remove a node or add a link.
    public event Action<AudioGraphNode>? NodeRemoved;

    public event Action<AudioGraphLink>? LinkAdded;

    public event Action<AudioGraphLink>? LinkRemoved;
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
}

internal sealed class FakePlayback : AudioGraphNode
{
    private static int _next = 1;

    public FakePlayback(string description)
        : base(_next++)
    {
        Type = (int)AudioNodeType.PlaybackDevice;
        Description = description;
        Name = description;
        Channels = [3];
        ApplyAudio([0f], false);
    }

    public override IAudioMeterInformation? Meter => null;

    public override void SetVolumes(IReadOnlyList<float> volumes) => ApplyAudio([.. volumes], Muted);

    public override void SetMuted(bool muted) => ApplyAudio([.. Volumes], muted);

    public void SetAudio(float volume, bool muted) => ApplyAudio([volume], muted);
}

/// <summary>The notification area without the session mutex or the Shell_TrayWnd window.</summary>
internal sealed class FakeTrayHost : ITrayHost
{
    public IReadOnlyCollection<TrayIcon> Icons => [];

    public event Action<TrayIcon>? Added;

    public event Action<TrayIcon>? Changed;

    public event Action<TrayIcon>? Removed;

#pragma warning disable CS0067 // SystemTray subscribes. These tests do not raise a balloon.
    public event Action<TrayIcon, string, string, uint>? Balloon;
#pragma warning restore CS0067

    public void Add(TrayIcon icon) => Added?.Invoke(icon);

    public void Change(TrayIcon icon) => Changed?.Invoke(icon);

    public void Remove(TrayIcon icon) => Removed?.Invoke(icon);

    public void Click(TrayIcon icon, TrayClick click, (int X, int Y)? at)
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
        AudioService = new AudioService(Loop, Log, Audio);
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
