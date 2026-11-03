using System.Collections.ObjectModel;
using System.ComponentModel;
using Winshell.Power;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>The power mode page: a choice for each mode, with the one in use chosen.</summary>
public sealed class QuickSettingsPowerMode
{
    private static readonly string[] Names = ["Best power efficiency", "Balanced", "Best performance"];

    private readonly PowerModeService _powerMode;

    internal QuickSettingsPowerMode(PowerModeService powerMode)
    {
        _powerMode = powerMode;
        for (var i = 0; i < Names.Length; i++)
        {
            var level = (PowerModeLevel)i;
            Choices.Add(new QuickSettingsChoice(null, Names[i], false, () => _powerMode.Mode = level));
        }
        powerMode.PropertyChanged += OnPowerMode;
        Publish();
    }

    public ObservableCollection<QuickSettingsChoice> Choices { get; } = [];

    /// <summary>What a mode is called, or empty for one this page does not list.</summary>
    internal static string Name(PowerModeLevel level)
    {
        var mode = (int)level;
        return mode >= 0 && mode < Names.Length ? Names[mode] : "";
    }

    private void OnPowerMode(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PowerModeService.Mode) or nameof(PowerModeService.Effective) or nameof(PowerModeService.Available))
            Publish();
    }

    private void Publish()
    {
        var mode = (int)_powerMode.Mode;
        for (var i = 0; i < Choices.Count; i++)
            Choices[i].Chosen = i == mode;
    }
}
