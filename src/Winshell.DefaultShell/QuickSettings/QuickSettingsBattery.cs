using System.ComponentModel;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The battery in the footer: its percent, and what Narrator reads for it.</summary>
public sealed class QuickSettingsBattery : ObservableObject
{
    private readonly PowerService _power;

    internal QuickSettingsBattery(PowerService power)
    {
        _power = power;
        power.DisplayDevice.PropertyChanged += OnPower;
        Publish();
    }

    /// <summary>What Narrator reads for the battery, including charging.</summary>
    public string Text { get; private set => Set(ref field, value); } = "Battery 0%";

    /// <summary>The percent drawn next to the battery glyph.</summary>
    public string Percent { get; private set => Set(ref field, value); } = "0%";

    private void OnPower(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PowerDevice.Percentage) or nameof(PowerDevice.State) or nameof(PowerDevice.IsPresent))
            Publish();
    }

    private void Publish()
    {
        var battery = _power.DisplayDevice;
        var percent = (int)Math.Round(battery.Percentage * 100, MidpointRounding.AwayFromZero);
        var charging = battery.State == PowerDeviceState.Charging;
        Percent = percent + "%";
        Text = "Battery " + percent + "%" + (charging ? ", charging" : "");
    }
}
