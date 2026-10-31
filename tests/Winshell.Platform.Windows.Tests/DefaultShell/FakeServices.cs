using Winshell.Platform.Windows.Services.Apps;
using Winshell.Platform.Windows.Services.Audio;
using Winshell.Platform.Windows.Services.Bluetooth;
using Winshell.Platform.Windows.Services.Media;
using Winshell.Platform.Windows.Services.Network;
using Winshell.Platform.Windows.Services.Power;
using Winshell.Platform.Windows.Services.Tray;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

// Stand-ins for the service singletons the default shell reads, so its tests never read or change this PC.
// Each one has the QML names of the real type it stands for; FakeServicesTests holds them to that.

internal static class FakeModel
{
    public static ObjectModel Of(QObject owner, params QObject[] values)
    {
        var model = new ObjectModel();
        model.SetOwner(owner);
        foreach (var value in values)
            model.Add(value);
        return model;
    }
}

public sealed class FakeNetworking : QObject
{
    private bool _wifiEnabled;
    private WifiRadioState _wifiState;
    private AirplaneModeState _airplaneMode = AirplaneModeState.Off;
    private NetworkConnectivity _connectivity;

    public FakeNetworking() => Devices = FakeModel.Of(this);

    [Qml] public ObjectModel Devices { get; }
    [Qml] public bool WifiEnabled { get { Track(); return _wifiEnabled; } internal set => SetField(ref _wifiEnabled, value); }
    [Qml] public WifiRadioState WifiState { get { Track(); return _wifiState; } internal set => SetField(ref _wifiState, value); }
    [Qml] public AirplaneModeState AirplaneMode { get { Track(); return _airplaneMode; } internal set => SetField(ref _airplaneMode, value); }
    [Qml] public NetworkConnectivity Connectivity { get { Track(); return _connectivity; } internal set => SetField(ref _connectivity, value); }
}

public sealed class FakeNetworkDevice : QObject
{
    private bool _connected;

    public FakeNetworkDevice(DeviceType type, params FakeNetwork[] networks)
    {
        Type = type;
        Networks = FakeModel.Of(this, networks);
    }

    [Qml] public DeviceType Type { get; }
    [Qml] public ObjectModel Networks { get; }
    [Qml] public bool Connected { get { Track(); return _connected; } internal set => SetField(ref _connected, value); }
}

public sealed class FakeNetwork(string name) : QObject
{
    private bool _connected;

    [Qml] public string Name => name;
    [Qml] public bool Connected { get { Track(); return _connected; } internal set => SetField(ref _connected, value); }
}

public sealed class FakeBluetooth : QObject
{
    private FakeBluetoothAdapter? _adapter;

    public FakeBluetooth() => Devices = FakeModel.Of(this);

    [Qml] public FakeBluetoothAdapter? DefaultAdapter { get { Track(); return _adapter; } internal set => SetField(ref _adapter, value); }
    [Qml] public ObjectModel Devices { get; }
}

public sealed class FakeBluetoothAdapter : QObject
{
    private bool _enabled;

    [Qml] public bool Enabled { get { Track(); return _enabled; } internal set => SetField(ref _enabled, value); }
}

public sealed class FakeBluetoothDevice(string name) : QObject
{
    private bool _connected;

    [Qml] public string Name => name;
    [Qml] public bool Connected { get { Track(); return _connected; } internal set => SetField(ref _connected, value); }
}

public sealed class FakePower : QObject
{
    [Qml] public FakePowerDevice DisplayDevice { get; } = new();
}

public sealed class FakePowerDevice : QObject
{
    private bool _isPresent;
    private double _percentage;
    private PowerDeviceState _state;

    [Qml] public bool IsPresent { get { Track(); return _isPresent; } internal set => SetField(ref _isPresent, value); }
    [Qml] public double Percentage { get { Track(); return _percentage; } internal set => SetField(ref _percentage, value); }
    [Qml] public PowerDeviceState State { get { Track(); return _state; } internal set => SetField(ref _state, value); }
}

public sealed class FakePowerMode : QObject
{
    private PowerModeLevel _mode = PowerModeLevel.Balanced;
    private EffectivePowerMode _effective = EffectivePowerMode.Balanced;
    private bool _available = true;

    [Qml] public PowerModeLevel Mode { get { Track(); return _mode; } set => SetField(ref _mode, value); }
    [Qml] public EffectivePowerMode Effective { get { Track(); return _effective; } internal set => SetField(ref _effective, value); }
    [Qml] public bool Available { get { Track(); return _available; } internal set => SetField(ref _available, value); }
}

public sealed class FakeBrightness : QObject
{
    public FakeBrightness() => Monitors = FakeModel.Of(this);

    [Qml] public ObjectModel Monitors { get; }
}

public sealed class FakeBrightnessMonitor(BrightnessConnection connection, int minimum, int maximum, string deviceName = "", string name = "") : QObject
{
    private int _brightness;

    [Qml] public string DeviceName => deviceName;
    [Qml] public string Name => name;
    [Qml] public BrightnessConnection Connection => connection;
    [Qml] public int Minimum => minimum;
    [Qml] public int Maximum => maximum;
    [Qml] public int Brightness { get { Track(); return _brightness; } set => SetField(ref _brightness, value); }
}

public sealed class FakeAudio : QObject
{
    private FakeAudioNode? _defaultPlaybackDevice;
    private FakeAudioNode? _defaultRecordingDevice;

    public FakeAudio() => Nodes = FakeModel.Of(this);

    [Qml] public ObjectModel Nodes { get; }
    [Qml] public FakeAudioNode? DefaultPlaybackDevice { get { Track(); return _defaultPlaybackDevice; } set => SetField(ref _defaultPlaybackDevice, value); }
    [Qml] public FakeAudioNode? DefaultRecordingDevice { get { Track(); return _defaultRecordingDevice; } set => SetField(ref _defaultRecordingDevice, value); }
}

public sealed class FakeAudioNode : QObject
{
    public FakeAudioNode(string description, bool isPlayback = true, bool isSession = false)
    {
        Description = description;
        IsPlayback = isPlayback;
        IsSession = isSession;
        Audio.SetOwner(this);
    }

    [Qml] public string Description { get; }
    [Qml] public bool IsPlayback { get; }
    [Qml] public bool IsSession { get; }
    [Qml] public FakeAudioVolume Audio { get; } = new();
}

public sealed class FakeAudioVolume : QObject
{
    private double _volume;
    private bool _muted;

    [Qml] public double Volume { get { Track(); return _volume; } set => SetField(ref _volume, value); }
    [Qml] public bool Muted { get { Track(); return _muted; } set => SetField(ref _muted, value); }
}

public sealed class FakeMedia : QObject
{
    public FakeMedia() => Sessions = FakeModel.Of(this);

    [Qml] public ObjectModel Sessions { get; }
}

/// <summary>A media session that records the controls pressed.</summary>
public sealed class FakeMediaSession(string appName, string title, string artist) : QObject
{
    private bool _isPlaying;
    private int _playbackState = 2;
    private string _trackArtUrl = "";

    public List<string> Calls { get; } = [];

    [Qml] public string AppName => appName;
    [Qml] public string TrackTitle => title;
    [Qml] public string TrackArtist => artist;
    [Qml] public string TrackArtUrl { get { Track(); return _trackArtUrl; } internal set => SetField(ref _trackArtUrl, value); }
    [Qml] public int PlaybackState { get { Track(); return _playbackState; } set => SetField(ref _playbackState, value); }

    [Qml]
    public bool IsPlaying
    {
        get { Track(); return _isPlaying; }
        set
        {
            if (SetField(ref _isPlaying, value))
                PlaybackState = value ? 1 : _playbackState == 1 ? 2 : _playbackState;
        }
    }

    [Qml] public bool CanTogglePlaying => true;
    [Qml] public bool CanGoNext => true;
    [Qml] public bool CanGoPrevious => false;

    [Qml]
    public void TogglePlaying()
    {
        Calls.Add("togglePlaying");
        IsPlaying = !IsPlaying;
    }

    [Qml] public void Next() => Calls.Add("next");

    [Qml] public void Previous() => Calls.Add("previous");
}

/// <summary>Records the URLs scripts open, instead of opening them.</summary>
public sealed class FakeUrls : IPlatformServices
{
    public List<string> Opened { get; } = [];

    public bool OpenUrl(string url)
    {
        Opened.Add(url);
        return true;
    }

    public void Quit(int code)
    {
    }
}

/// <summary>Every fake singleton, registered over the real ones.</summary>
public sealed class FakeServices
{
    public FakeNetworking Networking { get; } = new();
    public FakeBluetooth Bluetooth { get; } = new();
    public FakePower Power { get; } = new();
    public FakePowerMode PowerMode { get; } = new();
    public FakeBrightness Brightness { get; } = new();
    public FakeAudio Audio { get; } = new();
    public FakeMedia Media { get; } = new();
    public FakeWindowList WindowList { get; } = new();
    public FakeVirtualDesktops Desktops { get; } = new();
    public FakeApps Apps { get; } = new();
    public FakeJumpLists JumpLists { get; } = new();

    public FakeRecentDocuments Recent { get; } = new();
    public FakeSystemTray SystemTray { get; } = new();

    /// <summary>The real type each fake stands for, so their QML names can be compared.</summary>
    public static readonly (Type Fake, Type Real)[] Pairs =
    [
        (typeof(FakeNetworking), typeof(NetworkingService)),
        (typeof(FakeNetworkDevice), typeof(NetworkDevice)),
        (typeof(FakeNetwork), typeof(QmlNetwork)),
        (typeof(FakeBluetooth), typeof(BluetoothService)),
        (typeof(FakeBluetoothAdapter), typeof(BluetoothAdapter)),
        (typeof(FakeBluetoothDevice), typeof(BluetoothDevice)),
        (typeof(FakePower), typeof(PowerService)),
        (typeof(FakePowerDevice), typeof(PowerDevice)),
        (typeof(FakePowerMode), typeof(PowerModeService)),
        (typeof(FakeBrightness), typeof(BrightnessService)),
        (typeof(FakeBrightnessMonitor), typeof(BrightnessMonitor)),
        (typeof(FakeAudio), typeof(AudioService)),
        (typeof(FakeAudioNode), typeof(AudioNode)),
        (typeof(FakeAudioVolume), typeof(AudioVolume)),
        (typeof(FakeMedia), typeof(MediaService)),
        (typeof(FakeMediaSession), typeof(MediaSession)),
        (typeof(FakeWindowList), typeof(WindowListService)),
        (typeof(FakeAppWindow), typeof(AppWindow)),
        (typeof(FakeVirtualDesktops), typeof(VirtualDesktopsService)),
        (typeof(FakeVirtualDesktop), typeof(VirtualDesktop)),
        (typeof(FakeApps), typeof(AppsService)),
        (typeof(FakeAppEntry), typeof(AppEntry)),
        (typeof(FakeJumpLists), typeof(JumpListService)),
        (typeof(FakeRecentDocuments), typeof(RecentDocuments)),
        (typeof(FakeSystemTray), typeof(SystemTray)),
        (typeof(FakeTrayItem), typeof(SystemTrayItem)),
    ];

    public void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell.Network").Singleton(_ => Networking, "Networking");
        registry.Module("Winshell.Bluetooth").Singleton(_ => Bluetooth, "Bluetooth");
        registry.Module("Winshell.Power")
            .Singleton(_ => Power, "Power")
            .Singleton(_ => PowerMode, "PowerMode")
            .Singleton(_ => Brightness, "Brightness");
        registry.Module("Winshell.Audio").Singleton(_ => Audio, "Audio");
        registry.Module("Winshell.Media").Singleton(_ => Media, "Media");
        registry.Module("Winshell.Windows")
            .Singleton(_ => WindowList, "WindowList")
            .Singleton(_ => Desktops, "VirtualDesktops");
        registry.Module("Winshell.Apps")
            .Singleton(_ => Apps, "Apps")
            .Singleton(_ => JumpLists, "JumpLists")
            .Singleton(_ => Recent, "RecentDocuments");
        registry.Module("Winshell.Tray").Singleton(_ => SystemTray, "SystemTray");
    }
}
