using Winshell.Audio;
using Winshell.Bluetooth;
using Winshell.Media;
using Winshell.Network;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// What every taskbar's quick settings shows. One instance lives with the shell, and each bar opens it through a
/// <see cref="QuickSettingsPopup"/> of its own. Bluetooth is read only while a flyout is open.
/// </summary>
public sealed class QuickSettings
{
    /// <summary>The tile grid's columns: two 108-pixel cells fit its fixed 312-pixel content width.</summary>
    public const int TileColumns = 2;

    private readonly BluetoothService _bluetooth;
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
        _bluetooth = bluetooth;
        Status = status;
        // The services keep the tiles alive, and the pins list again whenever the tiles are read.
        var tiles = new QuickSettingsTiles(status, networking, bluetooth, powerMode);
        Pins = new QuickSettingsPins(tiles.All, layout);
        tiles.Refreshed += Pins.Refresh;
        Sound = new QuickSettingsSound(audio);
        Brightness = new QuickSettingsBrightness(brightness);
        Battery = new QuickSettingsBattery(power);
        PowerMode = new QuickSettingsPowerMode(powerMode);
        Media = new QuickSettingsMedia(media);
    }

    public SystemStatus Status { get; }

    public QuickSettingsPins Pins { get; }

    public QuickSettingsSound Sound { get; }

    public QuickSettingsBrightness Brightness { get; }

    public QuickSettingsBattery Battery { get; }

    public QuickSettingsPowerMode PowerMode { get; }

    public QuickSettingsMedia Media { get; }

    public event Action<string>? SettingsRequested;

    public QuickSettingsPopup CreatePopup() => new(this);

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
}
