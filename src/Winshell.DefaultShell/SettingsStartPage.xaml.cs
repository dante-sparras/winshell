using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>Recommended apps on or off, and the way to the pinned apps.</summary>
public sealed partial class SettingsStartPage : UserControl
{
    private readonly Preferences _preferences;
    private bool _applying;

    public SettingsStartPage(Preferences preferences)
    {
        _preferences = preferences;
        InitializeComponent();
        var recommended = SettingsCatalog.RequireSetting("startRecommended");
        RecommendedCard.Apply(recommended);
        Pins.Apply(SettingsCatalog.RequireSetting("startPins"));
        AutomationProperties.SetName(Recommended, recommended.Title);
        _preferences.PropertyChanged += OnPreferences;
        Show();
    }

    public event EventHandler? PinsRequested;

    public bool FocusSetting(string key)
    {
        var control = key switch
        {
            "startRecommended" => (Control)Recommended,
            "startPins" => Pins,
            _ => null,
        };
        if (control is null)
            return false;
        control.Focus(FocusState.Programmatic);
        control.StartBringIntoView();
        return true;
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.StartRecommended))
            Show();
    }

    private void Show()
    {
        _applying = true;
        Recommended.IsOn = _preferences.StartRecommended;
        _applying = false;
    }

    private void OnRecommendedToggled(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        _preferences.StartRecommended = Recommended.IsOn;
    }

    private void OnPins(object sender, RoutedEventArgs e) => PinsRequested?.Invoke(this, EventArgs.Empty);
}
