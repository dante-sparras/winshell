using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Audio;
using Winshell.Bluetooth;
using Winshell.Media;
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

/// <summary>A tile Winshell Settings and quick settings both know: its key, its name and its glyph.</summary>
public sealed record QuickSettingsKind(string Key, string Title, string Glyph);

/// <summary>One tile. A tile the PC cannot show stays out of <see cref="QuickSettings.Shown"/> and keeps its place.</summary>
public sealed class QuickSettingsTile : ObservableObject
{
    internal QuickSettingsTile(string key, string title, string glyph, string description, bool toggle, string settingsUri, string page)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        Label = title;
        Description = description;
        Toggle = toggle;
        SettingsUri = settingsUri;
        Page = page;
    }

    public string Key { get; }

    /// <summary>The tile's name. The label under it can show more, such as the network it is on.</summary>
    public string Title { get; }

    public string Glyph { get; }

    public string SettingsUri { get; }

    /// <summary>The page this tile opens, or empty when it opens Settings instead.</summary>
    public string Page { get; }

    public bool Toggle { get; }

    public bool HasPage => Page.Length > 0;

    public string Label { get; internal set => Set(ref field, value); }

    public string Description { get; internal set => Set(ref field, value); }

    public bool On { get; internal set => Set(ref field, value); }

    public bool Available { get; internal set => Set(ref field, value); } = true;
}

/// <summary>A power mode or a playback device on a page of choices.</summary>
public sealed class QuickSettingsChoice : ObservableObject
{
    private readonly Action _choose;

    internal QuickSettingsChoice(AudioNode? node, string text, bool chosen, Action choose)
    {
        Node = node;
        Text = text;
        Chosen = chosen;
        _choose = choose;
        if (node is not null)
            node.PropertyChanged += OnNode;
    }

    public AudioNode? Node { get; }

    public string Text { get; private set => Set(ref field, value); }

    public bool Chosen { get; internal set => Set(ref field, value); }

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
        node.PropertyChanged += OnAudio;
    }

    public AudioNode Node => _node;

    public string Label => _node.Description;

    public double Volume => _node.Volume;

    public string Glyph => StatusGlyphs.VolumeGlyph(_node.Muted, _node.Volume);

    public string MuteLabel => _node.Muted ? "Unmute" : "Mute";

    public void SetVolume(double value)
    {
        if (value == _node.Volume)
            return;
        _node.Volume = value;
    }

    public void ToggleMute() => _node.Muted = !_node.Muted;

    internal void Release()
    {
        _node.PropertyChanged -= OnNode;
        _node.PropertyChanged -= OnAudio;
    }

    private void OnNode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Description))
            Changed(nameof(Label));
    }

    private void OnAudio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioNode.Volume))
        {
            Changed(nameof(Volume));
            Changed(nameof(Glyph));
        }
        else if (e.PropertyName == nameof(AudioNode.Muted))
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

    internal QuickSettingsPopup(QuickSettings settings) => _settings = settings;

    public QuickSettings Settings => _settings;

    public string Page
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;
            Changed(nameof(ShowingTiles));
            Changed(nameof(ShowingPowerMode));
            Changed(nameof(ShowingOutput));
            Changed(nameof(ShowingVolume));
            Changed(nameof(ShowingAdd));
        }
    } = "";

    public bool IsOpen { get; private set => Set(ref field, value); }

    /// <summary>The tiles can be unpinned, added and reordered. Opening or closing the flyout leaves this.</summary>
    public bool Editing
    {
        get;
        private set
        {
            if (!Set(ref field, value))
                return;
            if (!value)
                ClearDrag();
            Changed(nameof(Browsing));
        }
    }

    /// <summary>The footer shows the battery, edit and settings while the tiles are not being edited.</summary>
    public bool Browsing => !Editing;

    public bool ShowingTiles => Page.Length == 0;

    public bool ShowingPowerMode => Page == "powerMode";

    public bool ShowingOutput => Page == "output";

    public bool ShowingVolume => Page == "volume";

    public bool ShowingAdd => Page == "add";

    /// <summary>The tile a drag picked up, as an index into <see cref="QuickSettings.Shown"/>, or -1.</summary>
    public int DragIndex { get; private set => Set(ref field, value); } = -1;

    /// <summary>Where the dragged tile would land, as an index into <see cref="QuickSettings.Shown"/>, or -1.</summary>
    public int DropIndex { get; private set => Set(ref field, value); } = -1;

    /// <summary>The tile the keyboard should be on after a move or an unpin, or empty.</summary>
    public string FocusKey { get; private set => Set(ref field, value); } = "";

    /// <summary>Shows the tiles. Opening one that was on a page comes back to them.</summary>
    public void Show()
    {
        Page = "";
        if (IsOpen)
            return;
        Editing = false;
        IsOpen = true;
        _settings.Opened();
    }

    public void Hide()
    {
        Page = "";
        Editing = false;
        if (!IsOpen)
            return;
        IsOpen = false;
        _settings.Closed();
    }

    /// <summary>Leaves a page, or edit mode when the tiles are showing. False means the flyout itself closes.</summary>
    public bool Escape()
    {
        if (Page.Length > 0)
        {
            Page = "";
            return true;
        }
        if (Editing)
        {
            Editing = false;
            return true;
        }
        return false;
    }

    public void Edit()
    {
        Page = "";
        Editing = true;
    }

    public void Done()
    {
        Page = "";
        Editing = false;
    }

    public void ShowAdd() => Page = "add";

    /// <summary>Pins a tile at the end and returns to the tiles.</summary>
    public void Add(string key)
    {
        _settings.Add(key);
        Page = "";
    }

    public void Activate(QuickSettingsTile tile)
    {
        // A tile does nothing while it is being edited. The drag surface takes the press.
        if (Editing)
            return;
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

    /// <summary>Moves the tile by an offset among the tiles on show, and keeps the keyboard on it.</summary>
    public void MoveBy(string key, int offset) => AskFocus(_settings.MoveBy(key, offset));

    /// <summary>Unpins a tile. From the keyboard, the focus goes to the tile that takes its place.</summary>
    public void Unpin(string key, bool fromKeyboard)
    {
        if (_settings.Unpin(key, fromKeyboard) is { } focus)
            AskFocus(focus);
    }

    public void BeginDrag(QuickSettingsTile tile)
    {
        var index = _settings.Shown.IndexOf(tile);
        DragIndex = index;
        DropIndex = index;
    }

    public void DragTo(double x, double y, double tileHeight)
    {
        if (DragIndex < 0)
            return;
        DropIndex = QuickSettings.IndexAt(x, y, _settings.Shown.Count, tileHeight);
    }

    /// <summary>Drops the tile. The order changes when it lands on another tile.</summary>
    public void EndDrag()
    {
        var from = DragIndex;
        var to = DropIndex;
        var shown = _settings.Shown;
        var key = from >= 0 && from < shown.Count ? shown[from].Key : null;
        var target = to >= 0 && to < shown.Count ? shown[to].Key : null;
        ClearDrag();
        if (key is not null && target is not null && from != to)
            _settings.Move(key, target);
    }

    /// <summary>How dim a tile is while another drag holds it. The one being dragged is half shown.</summary>
    public double DragOpacity(int index) => DragIndex == index ? 0.5 : 1;

    /// <summary>Where a dragged tile would land: -1 before this tile, 1 after it, 0 not here.</summary>
    public int DropSide(int index) =>
        DragIndex < 0 || DropIndex != index || DragIndex == index ? 0 : DropIndex < DragIndex ? -1 : 1;

    private void ClearDrag()
    {
        DragIndex = -1;
        DropIndex = -1;
    }

    private void AskFocus(string key)
    {
        // The same tile can move twice. The view still has to focus the new one.
        if (FocusKey == key)
            Changed(nameof(FocusKey));
        else
            FocusKey = key;
    }
}

/// <summary>
/// The tiles, volume, brightness, media and pages every taskbar's quick settings shows. One instance lives with the
/// shell. Bluetooth is read only while a flyout is open.
/// </summary>
public sealed class QuickSettings : ObservableObject
{
    /// <summary>The tiles Winshell knows, in the order a missing list uses.</summary>
    public static IReadOnlyList<QuickSettingsKind> Kinds { get; } =
    [
        new("wifi", "Wi-Fi", "\uE701"),
        new("bluetooth", "Bluetooth", "\uE702"),
        new("airplaneMode", "Airplane mode", "\uE709"),
        new("energySaver", "Energy saver", "\uE863"),
        new("powerMode", "Power mode", "\uEC4A"),
        new("nightLight", "Night light", "\uF08C"),
        new("accessibility", "Accessibility", "\uE776"),
    ];

    private static readonly string[] DefaultKeys = [.. Kinds.Select(static kind => kind.Key)];
    private static readonly string[] ModeNames = ["Best power efficiency", "Balanced", "Best performance"];

    private readonly SystemStatus _status;
    private readonly NetworkingService _networking;
    private readonly BluetoothService _bluetooth;
    private readonly AudioService _audio;
    private readonly PowerService _power;
    private readonly PowerModeService _powerMode;
    private readonly BrightnessService _brightness;
    private readonly QuickSettingsLayout _layout;
    private readonly MediaService _media;
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
    private readonly List<MediaSession> _mediaSessions = [];
    private BrightnessMonitor? _panel;
    private MediaSession? _session;
    private int _openFlyouts;

    public QuickSettings(
        SystemStatus status,
        NetworkingService networking,
        BluetoothService bluetooth,
        AudioService audio,
        PowerService power,
        PowerModeService powerMode,
        BrightnessService brightness,
        QuickSettingsLayout layout,
        MediaService media)
    {
        _status = status;
        _networking = networking;
        _bluetooth = bluetooth;
        _audio = audio;
        _power = power;
        _powerMode = powerMode;
        _brightness = brightness;
        _layout = layout;
        _media = media;
        _wifi = Tile("wifi", "Opens Wi-Fi settings", true, "ms-settings:network-wifi", "");
        _bluetoothTile = Tile("bluetooth", "Opens Bluetooth settings", true, "ms-settings:bluetooth", "");
        _airplane = Tile("airplaneMode", "Opens airplane mode settings", true, "ms-settings:network-airplanemode", "");
        _energySaver = Tile("energySaver", "Opens energy saver settings", true, "ms-settings:batterysaver", "");
        _powerModeTile = Tile("powerMode", "", false, "", "powerMode");
        _nightLight = Tile("nightLight", "Opens night light settings", false, "ms-settings:nightlight", "");
        _accessibility = Tile("accessibility", "Opens accessibility settings", false, "ms-settings:easeofaccess", "");
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
        media.Sessions.CollectionChanged += OnMediaSessions;
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
        WatchSessions();
    }

    public SystemStatus Status => _status;

    /// <summary>The pinned tiles this PC can show, in the saved order.</summary>
    public ObservableCollection<QuickSettingsTile> Shown { get; } = [];

    /// <summary>Tiles this PC can show that are not pinned, in the tile list's order.</summary>
    public ObservableCollection<QuickSettingsTile> Unpinned { get; } = [];

    /// <summary>Whether <see cref="Unpinned"/> has a tile the add page can put back.</summary>
    public bool HasUnpinned => Unpinned.Count > 0;

    public ObservableCollection<QuickSettingsChoice> Outputs { get; } = [];

    public ObservableCollection<QuickSettingsSession> Sessions { get; } = [];

    public ObservableCollection<QuickSettingsChoice> Modes { get; } = [];

    public double Volume { get; private set => Set(ref field, value); }

    public bool HasOutput { get; private set => Set(ref field, value); }

    public string MuteLabel { get; private set => Set(ref field, value); } = "Unmute";

    public double Brightness { get; private set => Set(ref field, value); }

    public bool HasBrightness { get; private set => Set(ref field, value); }

    /// <summary>What Narrator reads for the battery, including charging.</summary>
    public string BatteryText { get; private set => Set(ref field, value); } = "Battery 0%";

    /// <summary>The percent drawn next to the battery glyph.</summary>
    public string BatteryPercent { get; private set => Set(ref field, value); } = "0%";

    /// <summary>Whether the media card has a session to show.</summary>
    public bool HasMedia { get; private set => Set(ref field, value); }

    public string MediaApp { get; private set => Set(ref field, value); } = "";

    public string MediaTitle { get; private set => Set(ref field, value); } = "";

    public string MediaArtist { get; private set => Set(ref field, value); } = "";

    /// <summary>The track art url, or empty when the session has none.</summary>
    public string MediaArt { get; private set => Set(ref field, value); } = "";

    public bool HasMediaArt { get; private set => Set(ref field, value); }

    public bool CanMediaPrevious { get; private set => Set(ref field, value); }

    public bool CanMediaNext { get; private set => Set(ref field, value); }

    public bool CanMediaToggle { get; private set => Set(ref field, value); }

    public bool MediaPlaying { get; private set => Set(ref field, value); }

    public string MediaPlayGlyph { get; private set => Set(ref field, value); } = "\uE768";

    public string MediaPlayLabel { get; private set => Set(ref field, value); } = "Play";

    public event Action<string>? SettingsRequested;

    public QuickSettingsPopup CreatePopup() => new(this);

    public void SetVolume(double value)
    {
        var output = _audio.DefaultPlaybackDevice;
        if (output is null || value == output.Volume)
            return;
        output.Volume = value;
    }

    public void ToggleMute()
    {
        var output = _audio.DefaultPlaybackDevice;
        if (output is null)
            return;
        output.Muted = !output.Muted;
    }

    public void MediaPrevious() => _session?.Previous();

    public void MediaNext() => _session?.Next();

    public void MediaToggle() => _session?.TogglePlaying();

    /// <summary>Where a point in the tile grid lands. The grid is three tiles across, with 12 between them.</summary>
    public static int IndexAt(double x, double y, int count, double tileHeight)
    {
        if (count <= 0)
            return -1;
        const double tileWidth = 96;
        const double spacing = 12;
        var column = Math.Clamp((int)Math.Floor((x + spacing / 2) / (tileWidth + spacing)), 0, 2);
        var row = Math.Max(0, (int)Math.Floor((y + spacing / 2) / (tileHeight + spacing)));
        return Math.Min(count - 1, row * 3 + column);
    }

    /// <summary>Puts the tile with this key where the tile with another key is.</summary>
    public void Move(string key, string target)
    {
        var pinned = PinnedKeys();
        var at = pinned.IndexOf(target);
        // Before the target when moving back, after it when moving on: the index is taken before the key is removed.
        if (key == target || at < 0)
            return;
        var keys = pinned.Where(item => item != key).ToList();
        keys.Insert(at, key);
        SetPinned(keys);
    }

    /// <summary>Moves a tile by an offset among the tiles on show, and returns the key the keyboard stays on.</summary>
    public string MoveBy(string key, int offset)
    {
        var shown = Shown.Select(tile => tile.Key).ToList();
        var from = shown.IndexOf(key);
        var to = Math.Max(0, Math.Min(shown.Count - 1, from + offset));
        Move(key, shown[to]);
        return key;
    }

    /// <summary>Unpins a tile. From the keyboard, the result is the key that takes its place.</summary>
    public string? Unpin(string key, bool fromKeyboard)
    {
        var at = Shown.Select(tile => tile.Key).ToList().IndexOf(key);
        SetPinned(PinnedKeys().Where(item => item != key).ToList());
        if (!fromKeyboard || Shown.Count == 0)
            return null;
        return Shown[Math.Min(at, Shown.Count - 1)].Key;
    }

    /// <summary>Pins a tile at the end of the order.</summary>
    public void Add(string key) => SetPinned([.. PinnedKeys(), key]);

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
        if (e.PropertyName is nameof(AudioNode.Volume) or nameof(AudioNode.Muted))
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

    private void OnMediaSessions(object? sender, NotifyCollectionChangedEventArgs e) => WatchSessions();

    private void OnMediaSession(object? sender, PropertyChangedEventArgs e) => PublishMedia();

    private void WatchSessions()
    {
        foreach (var session in _mediaSessions)
            session.PropertyChanged -= OnMediaSession;
        _mediaSessions.Clear();
        _mediaSessions.AddRange(_media.Sessions);
        foreach (var session in _mediaSessions)
            session.PropertyChanged += OnMediaSession;
        PublishMedia();
    }

    /// <summary>The playing session, or the first one, or nothing. The card hides when there is nothing.</summary>
    private void PublishMedia()
    {
        _session = _media.Sessions.FirstOrDefault(session => session.IsPlaying) ?? _media.Sessions.FirstOrDefault();
        HasMedia = _session is not null;
        MediaApp = _session?.AppName ?? "";
        MediaTitle = _session?.TrackTitle ?? "";
        MediaArtist = _session?.TrackArtist ?? "";
        MediaArt = _session?.TrackArtUrl ?? "";
        HasMediaArt = MediaArt.Length > 0;
        CanMediaPrevious = _session?.CanGoPrevious == true;
        CanMediaNext = _session?.CanGoNext == true;
        CanMediaToggle = _session?.CanTogglePlaying == true;
        MediaPlaying = _session?.IsPlaying == true;
        MediaPlayGlyph = MediaPlaying ? "\uE769" : "\uE768";
        MediaPlayLabel = MediaPlaying ? "Pause" : "Play";
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
            _output.PropertyChanged -= OnOutputAudio;
        _output = output;
        if (_output is not null)
            _output.PropertyChanged += OnOutputAudio;
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

        _bluetoothTile.Label = _bluetooth.Devices.FirstOrDefault(device => device.Connected)?.Name ?? "Bluetooth";
        _bluetoothTile.On = _adapter?.Enabled ?? false;
        _bluetoothTile.Available = _adapter is not null;

        _airplane.On = _networking.AirplaneMode == AirplaneModeState.On;
        _energySaver.On = _powerMode.Effective == EffectivePowerMode.EnergySaver;

        var mode = (int)_powerMode.Mode;
        _powerModeTile.Description = mode >= 0 && mode < ModeNames.Length ? ModeNames[mode] : "";
        _powerModeTile.Available = _powerMode.Available;

        var keys = PinnedKeys();
        var shown = keys
            .Select(key => _tiles.FirstOrDefault(tile => tile.Key == key))
            .OfType<QuickSettingsTile>()
            .Where(tile => tile.Available)
            .ToList();
        Replace(Shown, shown);
        var hadUnpinned = HasUnpinned;
        var unpinned = _tiles.Where(tile => tile.Available && !keys.Contains(tile.Key)).ToList();
        Replace(Unpinned, unpinned);
        if (hadUnpinned != HasUnpinned)
            Changed(nameof(HasUnpinned));
    }

    private static QuickSettingsTile Tile(string key, string description, bool toggle, string settingsUri, string page)
    {
        foreach (var kind in Kinds)
        {
            if (kind.Key == key)
                return new QuickSettingsTile(kind.Key, kind.Title, kind.Glyph, description, toggle, settingsUri, page);
        }
        throw new InvalidOperationException($"Quick settings has no tile \"{key}\".");
    }

    private List<string> PinnedKeys() =>
        _layout.Pinned is { } pinned ? pinned.OfType<string>().ToList() : [.. DefaultKeys];

    /// <summary>Writes the order. A list the file already has is left, so a read does not write it back.</summary>
    private void SetPinned(IReadOnlyList<string> keys)
    {
        if (_layout.Pinned is { } current && current.Count == keys.Count && current.Zip(keys).All(pair => Equals(pair.First, pair.Second)))
            return;
        _layout.Pinned = [.. keys.Cast<object?>()];
    }

    private static void Replace<T>(ObservableCollection<T> target, List<T> next)
    {
        if (target.SequenceEqual(next))
            return;
        target.Clear();
        foreach (var item in next)
            target.Add(item);
    }

    private void PublishVolume()
    {
        var audio = _output;
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
        var nodes = _audio.Nodes.Where(node => node.IsPlayback && !node.IsSession).ToList();
        var chosen = _audio.DefaultPlaybackDevice;
        if (Outputs.Select(item => item.Node).SequenceEqual(nodes))
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
        var nodes = _audio.Nodes.Where(node => node.IsSession).ToList();
        if (Sessions.Select(session => session.Node).SequenceEqual(nodes))
            return;
        foreach (var session in Sessions)
            session.Release();
        Sessions.Clear();
        foreach (var node in nodes)
            Sessions.Add(new QuickSettingsSession(node));
    }
}
