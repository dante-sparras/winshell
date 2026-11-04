using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>Where the apps sit, auto-hide, and which displays have a taskbar. Explorer's taskbar is a link away.</summary>
public sealed partial class SettingsTaskbarPage : UserControl
{
    private readonly Preferences _preferences;
    private readonly Dictionary<string, Control> _focus = new(StringComparer.Ordinal);
    private bool _applying;

    public SettingsTaskbarPage(Preferences preferences)
    {
        _preferences = preferences;
        InitializeComponent();
        var alignment = SettingsCatalog.RequireSetting("taskbarAlignment");
        var autoHide = SettingsCatalog.RequireSetting("taskbarAutoHide");
        var allScreens = SettingsCatalog.RequireSetting("taskbarAllScreens");
        var apps = SettingsCatalog.RequireSetting("taskbarAppsOn");
        AlignmentCard.Apply(alignment);
        AutoHideCard.Apply(autoHide);
        AllScreensCard.Apply(allScreens);
        AppsCard.Apply(apps);
        Tray.Apply(SettingsCatalog.RequireSetting("trayIcons"));
        Fill(Alignment, SettingsCatalog.Alignments, alignment.Title);
        Fill(Apps, SettingsCatalog.TaskbarApps, apps.Title);
        AutomationProperties.SetHelpText(Apps, apps.Description);
        AutomationProperties.SetName(AutoHide, autoHide.Title);
        AutomationProperties.SetName(AllScreens, allScreens.Title);
        _focus[alignment.Key] = Alignment;
        _focus[autoHide.Key] = AutoHide;
        _focus[allScreens.Key] = AllScreens;
        _focus[apps.Key] = Apps;
        _focus["trayIcons"] = Tray;
        _preferences.PropertyChanged += OnPreferences;
        Show();
    }

    public bool FocusSetting(string key)
    {
        if (!_focus.TryGetValue(key, out var control))
            return false;
        control.Focus(FocusState.Programmatic);
        control.StartBringIntoView();
        return true;
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Preferences.TaskbarCentered) or nameof(Preferences.TaskbarAutoHide)
            or nameof(Preferences.TaskbarAllScreens) or nameof(Preferences.TaskbarAppsOn))
            Show();
    }

    private void Show()
    {
        Select(Alignment, SettingsCatalog.Alignments, _preferences.TaskbarCentered ? "center" : "left");
        Select(Apps, SettingsCatalog.TaskbarApps, _preferences.AppsOn);
        AppsCard.IsEnabled = _preferences.TaskbarAllScreens;
        _applying = true;
        AutoHide.IsOn = _preferences.TaskbarAutoHide;
        AllScreens.IsOn = _preferences.TaskbarAllScreens;
        _applying = false;
    }

    private void OnAlignmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || Alignment.SelectedIndex < 0)
            return;
        var centered = SettingsCatalog.Alignments[Alignment.SelectedIndex].Value == "center";
        if (centered != _preferences.TaskbarCentered)
            _preferences.TaskbarCentered = centered;
    }

    private void OnAutoHideToggled(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        _preferences.TaskbarAutoHide = AutoHide.IsOn;
    }

    private void OnAllScreensToggled(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        _preferences.TaskbarAllScreens = AllScreens.IsOn;
    }

    private void OnAppsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || Apps.SelectedIndex < 0)
            return;
        var picked = SettingsCatalog.TaskbarApps[Apps.SelectedIndex].Value;
        if (picked != _preferences.TaskbarAppsOn)
            _preferences.TaskbarAppsOn = picked;
    }

    private void Fill(ComboBox box, IReadOnlyList<SettingsOption> options, string name)
    {
        _applying = true;
        foreach (var option in options)
            box.Items.Add(option.Label);
        _applying = false;
        AutomationProperties.SetName(box, name);
    }

    private void Select(ComboBox box, IReadOnlyList<SettingsOption> options, string value)
    {
        _applying = true;
        box.SelectedIndex = SettingsCatalog.IndexOf(options, value);
        _applying = false;
    }
}
