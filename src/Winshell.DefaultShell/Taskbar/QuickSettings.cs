using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Audio;
using Winshell.Bluetooth;
using Winshell.Network;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Where quick settings sits: above the button that opened it, with the right edges aligned.</summary>
public static class QuickSettingsPlacement
{
    public const double Width = 360;

    public const double Gap = 8;

    public static QRect Above(QRect button, double height) =>
        new(button.Right - Width, button.Top - Gap - height, Width, height);
}

/// <summary>One tile. A tile the PC cannot show stays out of <see cref="QuickSettings.Shown"/> and keeps its place.</summary>
public sealed class QuickSettingsTile : ObservableObject
{
    private string _label;
    private string _description;
    private bool _on;
    private bool _available;

    internal QuickSettingsTile(string key, string title, string glyph, string description, bool toggle, string settingsUri, string page)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        _label = title;
        _description = description;
        Toggle = toggle;
        SettingsUri = settingsUri;
        Page = page;
        _available = true;
    }

    public string Key { get; }

    public string Title { get; }

    public string Glyph { get; }

    public string SettingsUri { get; }

    /// <summary>The page this tile opens, or empty when it opens Settings instead.</summary>
    public string Page { get; }

    public bool Toggle { get; }

    public bool HasPage => Page.Length > 0;

    public string Label
    {
        get => _label;
        internal set => Set(ref _label, value);
    }

    public string Description
    {
        get => _description;
        internal set => Set(ref _description, value);
    }

    public bool On
    {
        get => _on;
        internal set => Set(ref _on, value);
    }

    public bool Available
    {
        get => _available;
        internal set => Set(ref _available, value);
    }
}

/// <summary>A power mode or a playback device on a page of choices.</summary>
public sealed class QuickSettingsChoice : ObservableObject
{
    private readonly Action _choose;
    private string _text;
    private bool _chosen;

    internal QuickSettingsChoice(AudioNode? node, string text, bool chosen, Action choose)
    {
        Node = node;
        _text = text;
        _chosen = chosen;
        _choose = choose;
        if (node is not null)
            node.PropertyChanged += OnNode;
    }

    public AudioNode? Node { get; }

    public string Text
    {
        get => _text;
        private set => Set(ref _text, value);
    }

    public bool Chosen
    {
        get => _chosen;
        internal set => Set(ref _chosen, value);
    }

    public void Choose() => _choose();

    internal void Release()
    {
        if (Node is not null)
            Node.PropertyChanged -= OnNode;
    }

    private void OnNode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Description) && Node is not null)
            Text = Node.Description;
    }
}

/// <summary>One app's volume. Mute and the slider change this session only.</summary>
public sealed class QuickSettingsSession : ObservableObject
{
    private readonly AudioNode _node;

    internal QuickSettingsSession(AudioNode node)
    {
        _node = node;
        node.PropertyChanged += OnNode;
        node.Audio.PropertyChanged += OnAudio;
    }

    public AudioNode Node => _node;

    public string Label => _node.Description;

    public double Volume => _node.Audio.Volume;

    public string Glyph => StatusGlyphs.VolumeGlyph(_node.Audio.Muted, _node.Audio.Volume);

    public string MuteLabel => _node.Audio.Muted ? "Unmute" : "Mute";

    public void SetVolume(double value)
    {
        if (value == _node.Audio.Volume)
            return;
        _node.Audio.Volume = value;
    }

    public void ToggleMute() => _node.Audio.Muted = !_node.Audio.Muted;

    internal void Release()
    {
        _node.PropertyChanged -= OnNode;
        _node.Audio.PropertyChanged -= OnAudio;
    }

    private void OnNode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Description))
            Changed(nameof(Label));
    }

    private void OnAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioVolume.Volume))
        {
            Changed(nameof(Volume));
            Changed(nameof(Glyph));
        }
        else if (e.PropertyName == nameof(AudioVolume.Muted))
        {
            Changed(nameof(Glyph));
            Changed(nameof(MuteLabel));
        }
    }
}

/// <summary>
/// Quick settings for one taskbar. The tiles, the sliders and the pages are shared. Each bar has its own
/// <see cref="QuickSettingsPopup"/>.
/// </summary>
public sealed class QuickSettingsPopup : ObservableObject
{
    private readonly QuickSettings _settings;
    private string _page = "";
    private bool _open;

    internal QuickSettingsPopup(QuickSettings settings) => _settings = settings;

    public QuickSettings Settings => _settings;

    public string Page
    {
        get => _page;
        private set
        {
            if (!Set(ref _page, value))
                return;
            Changed(nameof(ShowingTiles));
            Changed(nameof(ShowingPowerMode));
            Changed(nameof(ShowingOutput));
            Changed(nameof(ShowingVolume));
        }
    }

    public bool IsOpen => _open;

    public bool ShowingTiles => _page.Length == 0;

    public bool ShowingPowerMode => _page == "powerMode";

    public bool ShowingOutput => _page == "output";

    public bool ShowingVolume => _page == "volume";

    /// <summary>Shows the tiles. Opening one that was on a page comes back to them.</summary>
    public void Show()
    {
        Page = "";
        if (_open)
            return;
        _open = true;
        Changed(nameof(IsOpen));
        _settings.Opened();
    }

    public void Hide()
    {
        Page = "";
        if (!_open)
            return;
        _open = false;
        Changed(nameof(IsOpen));
        _settings.Closed();
    }

    public void Toggle()
    {
        if (_open)
            Hide();
        else
            Show();
    }

    /// <summary>Leaves a page when one is open. False means the flyout itself closes.</summary>
    public bool Escape()
    {
        if (_page.Length == 0)
            return false;
        Page = "";
        return true;
    }

    public void Activate(QuickSettingsTile tile)
    {
        if (tile.Page.Length > 0)
            Page = tile.Page;
        else
            _settings.Open(tile.SettingsUri, this);
    }

    public void Back() => Page = "";

    public void ShowOutput() => Page = "output";

    public void ShowVolume() => Page = "volume";

    public void OpenBattery() => _settings.Open("ms-settings:batterysaver", this);

    public void OpenAllSettings() => _settings.Open("ms-settings:", this);

    public void OpenVolumeSettings() => _settings.Open("ms-settings:apps-volume", this);
}

/// <summary>
/// The tiles, volume, brightness and pages every taskbar's quick settings shows. One instance lives with the shell.
/// Bluetooth is read only while a flyout is open.
/// </summary>
public sealed class QuickSettings : ObservableObject
{
    private static readonly string[] DefaultKeys = ["wifi", "bluetooth", "airplaneMode", "energySaver", "powerMode", "nightLight", "accessibility"];
    private static readonly string[] ModeNames = ["Best power efficiency", "Balanced", "Best performance"];

    private readonly SystemStatus _status;
    private readonly NetworkingService _networking;
    private readonly BluetoothService _bluetooth;
    private readonly AudioService _audio;
    private readonly PowerService _power;
    private readonly PowerModeService _powerMode;
    private readonly BrightnessService _brightness;
    private readonly QuickSettingsLayout _layout;
    private readonly QuickSettingsTile _wifi;
    private readonly QuickSettingsTile _bluetoothTile;
    private readonly QuickSettingsTile _airplane;
    private readonly QuickSettingsTile _energySaver;
    private readonly QuickSettingsTile _powerModeTile;
    private readonly QuickSettingsTile _nightLight;
    private readonly QuickSettingsTile _accessibility;
    private readonly List<QuickSettingsTile> _tiles;
    private readonly List<BluetoothDevice> _devices = [];
    private BluetoothAdapter? _adapter;
    private AudioNode? _output;
    private BrightnessMonitor? _panel;
    private int _openFlyouts;
    private double _volume;
    private bool _hasOutput;
    private string _muteLabel = "Unmute";
    private double _brightnessValue;
    private bool _hasBrightness;
    private string _batteryText = "Battery 0%";
    private string _batteryPercent = "0%";

    public QuickSettings(
        SystemStatus status,
        NetworkingService networking,
        BluetoothService bluetooth,
        AudioService audio,
        PowerService power,
        PowerModeService powerMode,
        BrightnessService brightness,
        QuickSettingsLayout layout)
    {
        _status = status;
        _networking = networking;
        _bluetooth = bluetooth;
        _audio = audio;
        _power = power;
        _powerMode = powerMode;
        _brightness = brightness;
        _layout = layout;
        _wifi = new QuickSettingsTile("wifi", "Wi-Fi", "\uE701", "Opens Wi-Fi settings", true, "ms-settings:network-wifi", "");
        _bluetoothTile = new QuickSettingsTile("bluetooth", "Bluetooth", "\uE702", "Opens Bluetooth settings", true, "ms-settings:bluetooth", "");
        _airplane = new QuickSettingsTile("airplaneMode", "Airplane mode", "\uE709", "Opens airplane mode settings", true, "ms-settings:network-airplanemode", "");
        _energySaver = new QuickSettingsTile("energySaver", "Energy saver", "\uE863", "Opens energy saver settings", true, "ms-settings:batterysaver", "");
        _powerModeTile = new QuickSettingsTile("powerMode", "Power mode", "\uEC4A", "", false, "", "powerMode");
        _nightLight = new QuickSettingsTile("nightLight", "Night light", "\uF08C", "Opens night light settings", false, "ms-settings:nightlight", "");
        _accessibility = new QuickSettingsTile("accessibility", "Accessibility", "\uE776", "Opens accessibility settings", false, "ms-settings:easeofaccess", "");
        _tiles = [_wifi, _bluetoothTile, _airplane, _energySaver, _powerModeTile, _nightLight, _accessibility];
        for (var i = 0; i < ModeNames.Length; i++)
        {
            var level = (PowerModeLevel)i;
            Modes.Add(new QuickSettingsChoice(null, ModeNames[i], false, () => _powerMode.Mode = level));
        }

        status.PropertyChanged += OnStatus;
        networking.PropertyChanged += OnNetworking;
        networking.Devices.CollectionChanged += OnNetworkDevices;
        bluetooth.PropertyChanged += OnBluetoothService;
        bluetooth.Devices.CollectionChanged += OnBluetoothDevices;
        audio.PropertyChanged += OnAudioService;
        audio.Nodes.CollectionChanged += OnAudioNodes;
        power.DisplayDevice.PropertyChanged += OnPower;
        powerMode.PropertyChanged += OnPowerMode;
        brightness.Monitors.CollectionChanged += OnMonitors;
        layout.PropertyChanged += OnLayout;
        // The service starts reading when the shell does. Enumeration waits until a flyout is open.
        bluetooth.StopMonitoring();
        BindBluetooth();
        BindOutput();
        BindBrightness();
        PublishVolume();
        PublishBattery();
        PublishBrightness();
        PublishOutputs();
        PublishSessions();
        PublishModes();
        RefreshTiles();
    }

    public SystemStatus Status => _status;

    /// <summary>The pinned tiles this PC can show, in the saved order.</summary>
    public ObservableCollection<QuickSettingsTile> Shown { get; } = [];

    public ObservableCollection<QuickSettingsChoice> Outputs { get; } = [];

    public ObservableCollection<QuickSettingsSession> Sessions { get; } = [];

    public ObservableCollection<QuickSettingsChoice> Modes { get; } = [];

    public double Volume
    {
        get => _volume;
        private set => Set(ref _volume, value);
    }

    public bool HasOutput
    {
        get => _hasOutput;
        private set => Set(ref _hasOutput, value);
    }

    public string MuteLabel
    {
        get => _muteLabel;
        private set => Set(ref _muteLabel, value);
    }

    public double Brightness
    {
        get => _brightnessValue;
        private set => Set(ref _brightnessValue, value);
    }

    public bool HasBrightness
    {
        get => _hasBrightness;
        private set => Set(ref _hasBrightness, value);
    }

    /// <summary>What Narrator reads for the battery, including charging.</summary>
    public string BatteryText
    {
        get => _batteryText;
        private set => Set(ref _batteryText, value);
    }

    /// <summary>The percent drawn next to the battery glyph.</summary>
    public string BatteryPercent
    {
        get => _batteryPercent;
        private set => Set(ref _batteryPercent, value);
    }

    public event Action<string>? SettingsRequested;

    public QuickSettingsPopup CreatePopup() => new(this);

    public void SetVolume(double value)
    {
        var output = _audio.DefaultPlaybackDevice;
        if (output is null || value == output.Audio.Volume)
            return;
        output.Audio.Volume = value;
    }

    public void ToggleMute()
    {
        var output = _audio.DefaultPlaybackDevice;
        if (output is null)
            return;
        output.Audio.Muted = !output.Audio.Muted;
    }

    public void SetBrightness(double value)
    {
        if (_panel is null)
            return;
        var next = (int)Math.Round(_panel.Minimum + value * (_panel.Maximum - _panel.Minimum), MidpointRounding.AwayFromZero);
        if (next == _panel.Brightness)
            return;
        _panel.Brightness = next;
    }

    internal void Open(string uri, QuickSettingsPopup popup)
    {
        SettingsRequested?.Invoke(uri);
        popup.Hide();
    }

    internal void Opened()
    {
        if (_openFlyouts++ == 0)
            _bluetooth.StartMonitoring();
    }

    internal void Closed()
    {
        if (--_openFlyouts == 0)
            _bluetooth.StopMonitoring();
    }

    private void OnStatus(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemStatus.WifiName))
            RefreshTiles();
    }

    private void OnNetworking(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkingService.WifiEnabled) or nameof(NetworkingService.WifiState) or nameof(NetworkingService.AirplaneMode))
            RefreshTiles();
    }

    private void OnNetworkDevices(object? sender, NotifyCollectionChangedEventArgs e) => RefreshTiles();

    private void OnBluetoothService(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BluetoothService.DefaultAdapter))
            BindBluetooth();
    }

    private void OnBluetoothDevices(object? sender, NotifyCollectionChangedEventArgs e) => BindBluetooth();

    private void OnAdapter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BluetoothAdapter.Enabled))
            RefreshTiles();
    }

    private void OnDevice(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BluetoothDevice.Connected) or nameof(BluetoothDevice.Name))
            RefreshTiles();
    }

    private void OnAudioService(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AudioService.DefaultPlaybackDevice))
            return;
        BindOutput();
        PublishOutputs();
    }

    private void OnAudioNodes(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PublishOutputs();
        PublishSessions();
    }

    private void OnOutputAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioVolume.Volume) or nameof(AudioVolume.Muted))
            PublishVolume();
    }

    private void OnPower(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PowerDevice.Percentage) or nameof(PowerDevice.State) or nameof(PowerDevice.IsPresent))
            PublishBattery();
    }

    private void OnPowerMode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PowerModeService.Mode) or nameof(PowerModeService.Effective) or nameof(PowerModeService.Available))
        {
            PublishModes();
            RefreshTiles();
        }
    }

    private void OnMonitors(object? sender, NotifyCollectionChangedEventArgs e) => BindBrightness();

    private void OnPanel(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrightnessMonitor.Brightness) or nameof(BrightnessMonitor.Minimum) or nameof(BrightnessMonitor.Maximum) or nameof(BrightnessMonitor.Connection))
            PublishBrightness();
    }

    private void OnLayout(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSettingsLayout.Pinned))
            RefreshTiles();
    }

    private void BindBluetooth()
    {
        var adapter = _bluetooth.DefaultAdapter;
        if (!ReferenceEquals(adapter, _adapter))
        {
            if (_adapter is not null)
                _adapter.PropertyChanged -= OnAdapter;
            _adapter = adapter;
            if (_adapter is not null)
                _adapter.PropertyChanged += OnAdapter;
        }
        foreach (var device in _devices)
            device.PropertyChanged -= OnDevice;
        _devices.Clear();
        foreach (var device in _bluetooth.Devices)
        {
            device.PropertyChanged += OnDevice;
            _devices.Add(device);
        }
        RefreshTiles();
    }

    private void BindOutput()
    {
        var output = _audio.DefaultPlaybackDevice;
        if (ReferenceEquals(output, _output))
        {
            PublishVolume();
            return;
        }
        if (_output is not null)
            _output.Audio.PropertyChanged -= OnOutputAudio;
        _output = output;
        if (_output is not null)
            _output.Audio.PropertyChanged += OnOutputAudio;
        PublishVolume();
    }

    private void BindBrightness()
    {
        var panel = _brightness.Monitors.FirstOrDefault(monitor => monitor.Connection == BrightnessConnection.Internal);
        if (!ReferenceEquals(panel, _panel))
        {
            if (_panel is not null)
                _panel.PropertyChanged -= OnPanel;
            _panel = panel;
            if (_panel is not null)
                _panel.PropertyChanged += OnPanel;
        }
        PublishBrightness();
    }

    private void RefreshTiles()
    {
        var wifi = _networking.Devices.FirstOrDefault(device => device.Type == DeviceType.Wifi);
        _wifi.Label = _status.WifiName.Length > 0 ? _status.WifiName : "Wi-Fi";
        _wifi.On = _networking.WifiEnabled;
        _wifi.Available = wifi is not null || _networking.WifiState != WifiRadioState.Unknown;

        string? connected = null;
        foreach (var device in _bluetooth.Devices)
        {
            if (!device.Connected)
                continue;
            connected = device.Name;
            break;
        }
        _bluetoothTile.Label = connected ?? "Bluetooth";
        _bluetoothTile.On = _adapter?.Enabled ?? false;
        _bluetoothTile.Available = _adapter is not null;

        _airplane.On = _networking.AirplaneMode == AirplaneModeState.On;
        _energySaver.On = _powerMode.Effective == EffectivePowerMode.EnergySaver;

        var mode = (int)_powerMode.Mode;
        _powerModeTile.Description = mode >= 0 && mode < ModeNames.Length ? ModeNames[mode] : "";
        _powerModeTile.Available = _powerMode.Available;

        var keys = PinnedKeys();
        var shown = new List<QuickSettingsTile>(keys.Count);
        foreach (var key in keys)
        {
            var tile = _tiles.FirstOrDefault(item => item.Key == key);
            if (tile is { Available: true })
                shown.Add(tile);
        }
        if (Shown.Count == shown.Count)
        {
            var same = true;
            for (var i = 0; i < shown.Count; i++)
            {
                if (!ReferenceEquals(Shown[i], shown[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return;
        }
        Shown.Clear();
        foreach (var tile in shown)
            Shown.Add(tile);
    }

    private IReadOnlyList<string> PinnedKeys()
    {
        if (_layout.Pinned is not { } pinned)
            return DefaultKeys;
        var keys = new List<string>();
        foreach (var item in pinned)
        {
            if (item is string key)
                keys.Add(key);
        }
        return keys;
    }

    private void PublishVolume()
    {
        var audio = _output?.Audio;
        Volume = audio?.Volume ?? 0;
        HasOutput = _output is not null;
        MuteLabel = audio is null || audio.Muted ? "Unmute" : "Mute";
    }

    private void PublishBrightness()
    {
        HasBrightness = _panel is not null;
        if (_panel is null)
        {
            Brightness = 0;
            return;
        }
        Brightness = (_panel.Brightness - _panel.Minimum) / (double)Math.Max(1, _panel.Maximum - _panel.Minimum);
    }

    private void PublishBattery()
    {
        var battery = _power.DisplayDevice;
        var percent = (int)Math.Round(battery.Percentage * 100, MidpointRounding.AwayFromZero);
        var charging = battery.State == PowerDeviceState.Charging;
        BatteryPercent = percent + "%";
        BatteryText = "Battery " + percent + "%" + (charging ? ", charging" : "");
    }

    private void PublishModes()
    {
        var mode = (int)_powerMode.Mode;
        for (var i = 0; i < Modes.Count; i++)
            Modes[i].Chosen = i == mode;
    }

    private void PublishOutputs()
    {
        var nodes = new List<AudioNode>();
        foreach (var node in _audio.Nodes)
        {
            if (node.IsPlayback && !node.IsSession)
                nodes.Add(node);
        }
        var chosen = _audio.DefaultPlaybackDevice;
        if (SameNodes(Outputs, nodes))
        {
            foreach (var item in Outputs)
                item.Chosen = ReferenceEquals(item.Node, chosen);
            return;
        }
        foreach (var item in Outputs)
            item.Release();
        Outputs.Clear();
        foreach (var node in nodes)
            Outputs.Add(new QuickSettingsChoice(node, node.Description, ReferenceEquals(node, chosen), () => _audio.DefaultPlaybackDevice = node));
    }

    private void PublishSessions()
    {
        var nodes = new List<AudioNode>();
        foreach (var node in _audio.Nodes)
        {
            if (node.IsSession)
                nodes.Add(node);
        }
        if (Sessions.Count == nodes.Count)
        {
            var same = true;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (!ReferenceEquals(Sessions[i].Node, nodes[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return;
        }
        foreach (var session in Sessions)
            session.Release();
        Sessions.Clear();
        foreach (var node in nodes)
            Sessions.Add(new QuickSettingsSession(node));
    }

    private static bool SameNodes(ObservableCollection<QuickSettingsChoice> items, List<AudioNode> nodes)
    {
        if (items.Count != nodes.Count)
            return false;
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!ReferenceEquals(items[i].Node, nodes[i]))
                return false;
        }
        return true;
    }
}
