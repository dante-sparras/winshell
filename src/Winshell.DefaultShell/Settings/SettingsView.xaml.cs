using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Winshell.DefaultShell.Taskbar;
using Winshell.Input;

namespace Winshell.DefaultShell;

/// <summary>
/// Winshell Settings, as Windows 11 Settings lays it out: a pane under "Find a setting", and the page on the right
/// under a breadcrumb.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");

    private readonly Preferences _preferences;
    private readonly Shell _shell;
    private readonly SettingsNavigation _navigation = new();
    private readonly List<NavigationViewItem> _items = [];
    private readonly Dictionary<string, UIElement> _pages = new(StringComparer.Ordinal);
    private readonly List<SettingsCommand> _resultButtons = [];
    private readonly SettingsPersonalizationPage _personalization;
    private readonly SettingsTaskbarPage _taskbar;
    private readonly SettingsStartPage _start;
    private readonly SettingsNotificationsPage _notifications;
    private readonly SettingsStartupPage _startup;
    private readonly SettingsQuickSettingsPage _quickSettings;
    private readonly SettingsHotkeysPage _hotkeys;
    private readonly SettingsAboutPage _about;
    private readonly SettingsPinsPage _pins;
    private bool _applying;
    private string _crumbs = "";
    private string _shown = "";

    public SettingsView(Preferences preferences, Shell shell, Start start, QuickSettingsLayout layout, Hotkey startHotkey, Hotkey settingsHotkey)
    {
        _preferences = preferences;
        _shell = shell;
        InitializeComponent();
        _applying = true;
        foreach (var page in SettingsCatalog.Pages)
        {
            var item = new NavigationViewItem
            {
                Content = page.Title,
                Tag = page.Key,
                Icon = new FontIcon { Glyph = page.Glyph, FontFamily = Icons },
            };
            AutomationProperties.SetName(item, page.Title);
            _items.Add(item);
            Nav.MenuItems.Add(item);
        }
        _applying = false;

        _personalization = new SettingsPersonalizationPage(preferences, shell);
        _taskbar = new SettingsTaskbarPage(preferences);
        _start = new SettingsStartPage(preferences);
        _notifications = new SettingsNotificationsPage(preferences, shell);
        _startup = new SettingsStartupPage(shell);
        _start.PinsRequested += (_, _) => ShowPage("start", "pins");
        _quickSettings = new SettingsQuickSettingsPage(layout);
        _hotkeys = new SettingsHotkeysPage(preferences, startHotkey, settingsHotkey);
        _about = new SettingsAboutPage(preferences, shell);
        _pins = new SettingsPinsPage(start);
        Add("personalization", _personalization);
        Add("taskbar", _taskbar);
        Add("start", _start);
        Add("notifications", _notifications);
        Add("startup", _startup);
        Add("quickSettings", _quickSettings);
        Add("hotkeys", _hotkeys);
        Add("about", _about);
        Pages.Children.Add(_pins);

        preferences.PropertyChanged += OnPreferences;
        PreferencesError.IsOpen = preferences.Error.Length > 0;
        Sync(scroll: false);
    }

    /// <summary>Opens a page or the page of a setting. An unknown name is reported and the page stays.</summary>
    public bool Open(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        if (SettingsCatalog.Unknown(key) is { } warning)
        {
            _shell.Log.Warn("settings", warning);
            return false;
        }
        _navigation.Open(key);
        Sync(scroll: true);
        return true;
    }

    public void FocusSearch() => Search.Focus(FocusState.Programmatic);

    public void FocusPending() => FocusPage(_navigation.FocusKey);

    public void RefreshStartup() => _startup.RefreshFromWindows();

    private void Add(string key, UIElement page)
    {
        _pages[key] = page;
        Pages.Children.Add(page);
    }

    private void ShowPage(string page, string? subpage = null)
    {
        _navigation.Show(page, subpage);
        Sync(scroll: true);
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_applying)
            return;
        if (args.SelectedItem is NavigationViewItem { Tag: string key })
            ShowPage(key);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_applying)
            return;
        _navigation.Search(sender.Text);
        Sync(scroll: false);
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_navigation.Results.Count == 0)
            return;
        _navigation.Open(_navigation.Results[0].Key);
        Sync(scroll: true);
        FocusPage(_navigation.FocusKey);
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down && _navigation.Searching && _resultButtons.Count > 0)
        {
            _resultButtons[0].Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape && Search.Text.Length > 0)
        {
            Search.Text = "";
            e.Handled = true;
        }
    }

    private void OnResultKey(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Control current)
            return;
        var index = _resultButtons.IndexOf((SettingsCommand)current);
        if (index < 0)
            return;
        if (e.Key == VirtualKey.Up)
        {
            if (index == 0)
                Search.Focus(FocusState.Programmatic);
            else
                _resultButtons[index - 1].Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Down && index + 1 < _resultButtons.Count)
        {
            _resultButtons[index + 1].Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.Error))
            PreferencesError.IsOpen = _preferences.Error.Length > 0;
    }

    private void OnScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
            return;
        var width = Math.Min(SettingsCatalog.ContentMaxWidth, Math.Max(0, e.NewSize.Width - 80));
        if (!double.IsNaN(Body.Width) && Math.Abs(Body.Width - width) < 0.5)
            return;
        Body.Width = width;
    }

    private void Sync(bool scroll)
    {
        var shown = _navigation.Shown;
        foreach (var (key, page) in _pages)
            page.Visibility = shown == key + "/" ? Visibility.Visible : Visibility.Collapsed;
        _pins.Visibility = shown == "start/pins" ? Visibility.Visible : Visibility.Collapsed;

        _applying = true;
        if (Search.Text != _navigation.Query)
            Search.Text = _navigation.Query;
        Nav.SelectedItem = _navigation.Searching ? null : _items.Find(item => item.Tag as string == _navigation.Page);
        _applying = false;

        RebuildCrumbs();
        RebuildResults();

        var changed = shown != _shown;
        _shown = shown;
        if (changed && shown == "startup/")
            _startup.RefreshFromWindows();
        if (scroll && changed)
            Scroll.ChangeView(null, 0, null, true);
    }

    private void RebuildCrumbs()
    {
        var crumbs = _navigation.Crumbs;
        var signature = string.Join('|', crumbs.Select(crumb => $"{crumb.Key}:{crumb.Link}:{crumb.Title}"));
        if (signature == _crumbs)
            return;
        _crumbs = signature;
        Breadcrumb.Children.Clear();
        for (var i = 0; i < crumbs.Count; i++)
        {
            if (i > 0)
            {
                var chevron = new TextBlock
                {
                    Text = "\uE76C",
                    FontFamily = Icons,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                    IsHitTestVisible = false,
                };
                AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
                Breadcrumb.Children.Add(chevron);
            }
            var crumb = crumbs[i];
            if (crumb.Link)
            {
                var link = new HyperlinkButton
                {
                    Content = crumb.Title,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    UseSystemFocusVisuals = true,
                };
                AutomationProperties.SetName(link, crumb.Title);
                var key = crumb.Key;
                link.Click += (_, _) => ShowPage(key);
                Breadcrumb.Children.Add(link);
            }
            else
            {
                var text = new TextBlock
                {
                    Text = crumb.Title,
                    FontSize = 28,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                AutomationProperties.SetName(text, crumb.Title);
                AutomationProperties.SetHeadingLevel(text, AutomationHeadingLevel.Level1);
                Breadcrumb.Children.Add(text);
            }
        }
    }

    private void RebuildResults()
    {
        while (Results.Children.Count > 1)
            Results.Children.RemoveAt(Results.Children.Count - 1);
        _resultButtons.Clear();
        if (!_navigation.Searching)
        {
            Results.Visibility = Visibility.Collapsed;
            return;
        }
        Results.Visibility = Visibility.Visible;
        if (_navigation.Results.Count == 0)
        {
            Empty.Text = SettingsCatalog.NoResults(_navigation.Query);
            AutomationProperties.SetName(Empty, Empty.Text);
            Empty.Visibility = Visibility.Visible;
            return;
        }
        Empty.Visibility = Visibility.Collapsed;
        foreach (var result in _navigation.Results)
        {
            var button = new SettingsCommand();
            button.Apply(result, SettingsCatalog.ResultText(result));
            var key = result.Key;
            button.Click += (_, _) =>
            {
                _navigation.Open(key);
                Sync(scroll: true);
                FocusPage(_navigation.FocusKey);
            };
            button.PreviewKeyDown += OnResultKey;
            _resultButtons.Add(button);
            Results.Children.Add(button);
        }
    }

    private void FocusPage(string? key)
    {
        if (key is not { Length: > 0 } || _navigation.Searching)
            return;
        _ = _navigation.Page switch
        {
            "personalization" => _personalization.FocusSetting(key),
            "taskbar" => _taskbar.FocusSetting(key),
            "start" when _navigation.Subpage.Length == 0 => _start.FocusSetting(key),
            "quickSettings" => _quickSettings.FocusSetting(key),
            "notifications" => _notifications.FocusSetting(key),
            "hotkeys" => _hotkeys.FocusSetting(key),
            "startup" => _startup.FocusSetting(key),
            "about" => _about.FocusSetting(key),
            _ => false,
        };
    }
}
