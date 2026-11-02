using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Winshell.Input;

namespace Winshell.DefaultShell;

/// <summary>
/// The shell's hotkeys, each a sequence the user types, and the switch for desktop chords. The chords themselves are
/// registered elsewhere. Empty turns a hotkey off. One Windows or another app already owns says so.
/// </summary>
public sealed partial class SettingsHotkeysPage : UserControl
{
    private readonly Preferences _preferences;
    private readonly Hotkey _startHotkey;
    private readonly Hotkey _settingsHotkey;
    private readonly string _startTitle;
    private readonly string _settingsTitle;
    private string _startRefused = "";
    private string _settingsRefused = "";
    private bool _applying;

    public SettingsHotkeysPage(Preferences preferences, Hotkey startHotkey, Hotkey settingsHotkey)
    {
        _preferences = preferences;
        _startHotkey = startHotkey;
        _settingsHotkey = settingsHotkey;
        InitializeComponent();
        var start = SettingsCatalog.RequireSetting("hotkeyStart");
        var settings = SettingsCatalog.RequireSetting("hotkeySettings");
        var chords = SettingsCatalog.RequireSetting("desktopChords");
        _startTitle = start.Title;
        _settingsTitle = settings.Title;
        StartCard.Apply(start);
        SettingsCard.Apply(settings);
        ChordsCard.Apply(chords);
        AutomationProperties.SetName(StartField, start.Title);
        AutomationProperties.SetName(SettingsField, settings.Title);
        AutomationProperties.SetName(Chords, chords.Title);
        preferences.PropertyChanged += OnPreferences;
        startHotkey.PropertyChanged += OnHotkey;
        settingsHotkey.PropertyChanged += OnHotkey;
        Show();
    }

    public bool FocusSetting(string key)
    {
        var control = key switch
        {
            "hotkeyStart" => (Control)StartField,
            "hotkeySettings" => SettingsField,
            "desktopChords" => Chords,
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
        if (e.PropertyName is nameof(Preferences.StartHotkey) or nameof(Preferences.SettingsHotkey) or nameof(Preferences.DesktopChords))
            Show();
    }

    private void OnHotkey(object? sender, PropertyChangedEventArgs e) => Show();

    private void OnChordsToggled(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        _preferences.DesktopChords = Chords.IsOn;
    }

    private void OnFieldKey(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox field)
            return;
        if (e.Key == VirtualKey.Enter)
        {
            Apply(field);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Restore(field);
            e.Handled = true;
        }
    }

    private void OnFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox field)
            Apply(field);
    }

    private void Apply(TextBox field)
    {
        if (_applying)
            return;
        var start = field == StartField;
        var saved = Sequence(start);
        var commit = SettingsHotkeys.Commit(field.Text, saved, start ? _settingsTitle : _startTitle, Sequence(!start));
        if (commit is { } next)
        {
            if (start)
                _startRefused = next.Refused;
            else
                _settingsRefused = next.Refused;
            if (next.Sequence != saved)
                SetSequence(start, next.Sequence);
        }
        _applying = true;
        field.Text = Sequence(start);
        _applying = false;
        Show();
    }

    private void Restore(TextBox field)
    {
        _applying = true;
        field.Text = Sequence(field == StartField);
        _applying = false;
    }

    private void Show()
    {
        StartCard.Description = SettingsHotkeys.Description(_startRefused, _preferences.StartHotkey, State(_startHotkey));
        SettingsCard.Description = SettingsHotkeys.Description(_settingsRefused, _preferences.SettingsHotkey, State(_settingsHotkey));
        AutomationProperties.SetHelpText(StartField, StartCard.Description);
        AutomationProperties.SetHelpText(SettingsField, SettingsCard.Description);
        if (!_applying && StartField.FocusState == FocusState.Unfocused && StartField.Text != _preferences.StartHotkey)
            StartField.Text = _preferences.StartHotkey;
        if (!_applying && SettingsField.FocusState == FocusState.Unfocused && SettingsField.Text != _preferences.SettingsHotkey)
            SettingsField.Text = _preferences.SettingsHotkey;
        _applying = true;
        Chords.IsOn = _preferences.DesktopChords;
        _applying = false;
    }

    private string Sequence(bool start) => start ? _preferences.StartHotkey : _preferences.SettingsHotkey;

    private void SetSequence(bool start, string sequence)
    {
        if (start)
            _preferences.StartHotkey = sequence;
        else
            _preferences.SettingsHotkey = sequence;
    }

    private static SettingsHotkeyState State(Hotkey hotkey) => new(true, hotkey.Error, hotkey.Taken, hotkey.Active);
}
