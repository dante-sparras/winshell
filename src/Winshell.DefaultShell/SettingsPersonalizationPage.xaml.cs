using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Winshell.DefaultShell;

/// <summary>Light or dark, the accent color and the material. They change Winshell only.</summary>
public sealed partial class SettingsPersonalizationPage : UserControl
{
    private readonly Preferences _preferences;
    private readonly Shell _shell;
    private readonly List<AccentSwatch> _swatches = [];
    private readonly Dictionary<string, Control> _focus = new(StringComparer.Ordinal);
    private bool _applying;
    private int _columns;

    public SettingsPersonalizationPage(Preferences preferences, Shell shell)
    {
        _preferences = preferences;
        _shell = shell;
        InitializeComponent();
        var scheme = SettingsCatalog.RequireSetting("colorScheme");
        var accent = SettingsCatalog.RequireSetting("accentColor");
        var material = SettingsCatalog.RequireSetting("material");
        SchemeCard.Apply(scheme);
        AccentCard.Apply(accent);
        MaterialCard.Apply(material);
        Colors.Apply(SettingsCatalog.RequireSetting("windowsColors"));
        Fill(Scheme, SettingsCatalog.ColorSchemes, scheme.Title);
        Fill(Accent, SettingsCatalog.AccentChoices, accent.Title);
        Fill(Material, SettingsCatalog.Materials, material.Title);
        _focus[scheme.Key] = Scheme;
        _focus[accent.Key] = Accent;
        _focus[material.Key] = Material;
        _focus["windowsColors"] = Colors;
        foreach (var item in SettingsCatalog.Accents)
        {
            var swatch = new AccentSwatch(item.Color, item.Name);
            swatch.Picked += OnSwatchPicked;
            _swatches.Add(swatch);
            SwatchGrid.Children.Add(swatch);
        }
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
        if (e.PropertyName is nameof(Preferences.ColorScheme) or nameof(Preferences.AccentColor) or nameof(Preferences.Backdrop))
            Show();
    }

    private void Show()
    {
        Select(Scheme, SettingsCatalog.ColorSchemes, _preferences.ValidColorScheme);
        Select(Accent, SettingsCatalog.AccentChoices, _preferences.ValidAccent ? "manual" : "windows");
        Select(Material, SettingsCatalog.Materials, _preferences.Backdrop == "mica" ? "mica" : "acrylic");
        Swatches.Visibility = _preferences.ValidAccent ? Visibility.Visible : Visibility.Collapsed;
        _applying = true;
        foreach (var swatch in _swatches)
            swatch.IsChosen = string.Equals(swatch.Color, _preferences.AccentColor, StringComparison.OrdinalIgnoreCase);
        _applying = false;
    }

    private void OnSchemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || Scheme.SelectedIndex < 0)
            return;
        var picked = SettingsCatalog.ColorSchemes[Scheme.SelectedIndex].Value;
        if (picked != _preferences.ColorScheme)
            _preferences.ColorScheme = picked;
    }

    private void OnAccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || Accent.SelectedIndex < 0)
            return;
        var picked = SettingsCatalog.AccentChoices[Accent.SelectedIndex].Value;
        var next = SettingsCatalog.ChooseAccent(picked, _shell.SystemAccent);
        if (!string.Equals(next, _preferences.AccentColor, StringComparison.OrdinalIgnoreCase))
            _preferences.AccentColor = next;
    }

    private void OnMaterialChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || Material.SelectedIndex < 0)
            return;
        var picked = SettingsCatalog.Materials[Material.SelectedIndex].Value;
        if (picked != _preferences.Backdrop)
            _preferences.Backdrop = picked;
    }

    private void OnSwatchPicked(object? sender, EventArgs e)
    {
        if (_applying || sender is not AccentSwatch swatch)
            return;
        if (!string.Equals(_preferences.AccentColor, swatch.Color, StringComparison.OrdinalIgnoreCase))
            _preferences.AccentColor = swatch.Color;
    }

    private void OnSwatchSize(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
            return;
        var columns = SettingsCatalog.SwatchColumns(e.NewSize.Width);
        if (columns == _columns)
            return;
        _columns = columns;
        SwatchGrid.ColumnDefinitions.Clear();
        SwatchGrid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            SwatchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rows = (int)Math.Ceiling(_swatches.Count / (double)columns);
        for (var row = 0; row < rows; row++)
            SwatchGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < _swatches.Count; i++)
        {
            Grid.SetColumn(_swatches[i], i % columns);
            Grid.SetRow(_swatches[i], i / columns);
        }
    }

    private void OnSwatchKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down))
            return;
        if (e.OriginalSource is not DependencyObject source)
            return;
        var index = -1;
        for (var i = 0; i < _swatches.Count; i++)
        {
            if (_swatches[i].Holds(source))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return;
        var columns = Math.Max(1, _columns);
        var next = e.Key switch
        {
            VirtualKey.Left => index - 1,
            VirtualKey.Right => index + 1,
            VirtualKey.Up => index - columns,
            VirtualKey.Down => index + columns,
            _ => -1,
        };
        e.Handled = true;
        if (next >= 0 && next < _swatches.Count)
            _swatches[next].FocusChoice();
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
