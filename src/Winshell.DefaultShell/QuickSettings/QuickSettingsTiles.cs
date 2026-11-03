using System.Collections.Specialized;
using System.ComponentModel;
using Winshell.Bluetooth;
using Winshell.Network;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>Every tile, kept to what Wi-Fi, Bluetooth and power mode report: its label, whether it is on, whether the PC can show it.</summary>
internal sealed class QuickSettingsTiles
{
    private readonly SystemStatus _status;
    private readonly NetworkingService _networking;
    private readonly BluetoothService _bluetooth;
    private readonly PowerModeService _powerMode;
    private readonly QuickSettingsTile _wifi;
    private readonly QuickSettingsTile _bluetoothTile;
    private readonly QuickSettingsTile _airplane;
    private readonly QuickSettingsTile _energySaver;
    private readonly QuickSettingsTile _powerModeTile;
    private readonly List<BluetoothDevice> _devices = [];
    private BluetoothAdapter? _adapter;

    public QuickSettingsTiles(SystemStatus status, NetworkingService networking, BluetoothService bluetooth, PowerModeService powerMode)
    {
        _status = status;
        _networking = networking;
        _bluetooth = bluetooth;
        _powerMode = powerMode;
        _wifi = Tile("wifi", "Opens Wi-Fi settings", true, "ms-settings:network-wifi", "");
        _bluetoothTile = Tile("bluetooth", "Opens Bluetooth settings", true, "ms-settings:bluetooth", "");
        _airplane = Tile("airplaneMode", "Opens airplane mode settings", true, "ms-settings:network-airplanemode", "");
        _energySaver = Tile("energySaver", "Opens energy saver settings", true, "ms-settings:batterysaver", "");
        _powerModeTile = Tile("powerMode", "", false, "", "powerMode");
        All =
        [
            _wifi,
            _bluetoothTile,
            _airplane,
            _energySaver,
            _powerModeTile,
            Tile("nightLight", "Opens night light settings", false, "ms-settings:nightlight", ""),
            Tile("accessibility", "Opens accessibility settings", false, "ms-settings:easeofaccess", ""),
        ];

        status.PropertyChanged += OnStatus;
        networking.PropertyChanged += OnNetworking;
        networking.Devices.CollectionChanged += OnNetworkDevices;
        bluetooth.PropertyChanged += OnBluetoothService;
        bluetooth.Devices.CollectionChanged += OnBluetoothDevices;
        powerMode.PropertyChanged += OnPowerMode;
        BindBluetooth();
    }

    /// <summary>The tiles in the order <see cref="QuickSettingsKind.All"/> has them.</summary>
    public IReadOnlyList<QuickSettingsTile> All { get; }

    /// <summary>The tiles were read again, so which of them the PC can show may have changed.</summary>
    public event Action? Refreshed;

    private void OnStatus(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemStatus.WifiName))
            Refresh();
    }

    private void OnNetworking(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkingService.WifiEnabled) or nameof(NetworkingService.WifiState) or nameof(NetworkingService.AirplaneMode))
            Refresh();
    }

    private void OnNetworkDevices(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnBluetoothService(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BluetoothService.DefaultAdapter))
            BindBluetooth();
    }

    private void OnBluetoothDevices(object? sender, NotifyCollectionChangedEventArgs e) => BindBluetooth();

    private void OnAdapter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BluetoothAdapter.Enabled))
            Refresh();
    }

    private void OnDevice(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BluetoothDevice.Connected) or nameof(BluetoothDevice.Name))
            Refresh();
    }

    private void OnPowerMode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PowerModeService.Mode) or nameof(PowerModeService.Effective) or nameof(PowerModeService.Available))
            Refresh();
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
        Refresh();
    }

    private void Refresh()
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

        _powerModeTile.Description = QuickSettingsPowerMode.Name(_powerMode.Mode);
        _powerModeTile.Available = _powerMode.Available;
        Refreshed?.Invoke();
    }

    private static QuickSettingsTile Tile(string key, string description, bool toggle, string settingsUri, string page)
    {
        foreach (var kind in QuickSettingsKind.All)
        {
            if (kind.Key == key)
                return new QuickSettingsTile(kind.Key, kind.Title, kind.Glyph, description, toggle, settingsUri, page);
        }
        throw new InvalidOperationException($"Quick settings has no tile \"{key}\".");
    }
}
