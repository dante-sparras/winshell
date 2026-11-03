using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Winshell.Notifications;

namespace Winshell.DefaultShell;

/// <summary>Do not disturb as Windows reports it, and whether the notification center shows each app.</summary>
public sealed partial class SettingsNotificationsPage : UserControl
{
    private readonly Preferences _preferences;
    private readonly Shell _shell;
    private readonly NotificationListener _listener;
    private readonly Dictionary<string, (SettingsCard Card, ToggleSwitch Switch)> _rows = new(StringComparer.Ordinal);
    private IReadOnlyList<NotificationApp> _apps = [];
    private bool _applying;
    private bool _queued;

    public SettingsNotificationsPage(Preferences preferences, Shell shell)
    {
        _preferences = preferences;
        _shell = shell;
        _listener = shell.Notifications;
        InitializeComponent();
        AppsCard.Apply(SettingsCatalog.RequireSetting("notificationApps"));
        AppsCard.IsTabStop = true;
        Disturb.Apply(SettingsCatalog.RequireSetting("doNotDisturb"));
        var state = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(state, AccessibilityView.Raw);
        Disturb.Card.TrailContent = state;
        State = state;
        _listener.PropertyChanged += OnListener;
        _listener.Groups.CollectionChanged += OnGroups;
        _preferences.PropertyChanged += OnPreferences;
        ShowState();
        ReadApps();
    }

    private TextBlock State { get; }

    public bool FocusSetting(string key)
    {
        var control = key switch
        {
            "doNotDisturb" => (Control?)Disturb,
            "notificationApps" => AppsCard,
            _ => null,
        };
        if (control is null)
            return false;
        control.Focus(FocusState.Programmatic);
        control.StartBringIntoView();
        return true;
    }

    private void OnListener(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationListener.DoNotDisturbState))
            ShowState();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.HiddenNotificationApps))
            QueueApps();
    }

    private void OnGroups(object? sender, NotifyCollectionChangedEventArgs e) => QueueApps();

    private void ShowState()
    {
        State.Text = NotificationAppList.DoNotDisturbText(_listener.DoNotDisturbState);
    }

    // Groups change one app at a time. Read the whole list once the burst has landed.
    private void QueueApps()
    {
        if (_queued)
            return;
        _queued = true;
        _shell.Loop.Post(ReadApps);
    }

    private void ReadApps()
    {
        _queued = false;
        var found = NotificationAppList.Found(Groups(), Hidden());
        var merged = NotificationAppList.Merge(_apps, found);
        var grown = merged.Count != _apps.Count;
        _apps = merged;
        if (grown)
        {
            foreach (var app in merged)
            {
                if (_rows.ContainsKey(app.Id))
                    continue;
                var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0 };
                AutomationProperties.SetName(toggle, $"Show notifications from {app.Name}");
                var id = app.Id;
                var name = app.Name;
                toggle.Toggled += (_, _) =>
                {
                    if (_applying)
                        return;
                    _preferences.SetNotificationsFrom(id, name, toggle.IsOn);
                };
                var card = new SettingsCard
                {
                    Glyph = "\uEA8F",
                    Title = app.Name,
                    TrailContent = toggle,
                };
                _rows[app.Id] = (card, toggle);
                Apps.Children.Add(card);
            }
            for (var i = 0; i < merged.Count; i++)
            {
                var card = _rows[merged[i].Id].Card;
                var index = Apps.Children.IndexOf(card);
                if (index >= 0 && index != i)
                    Apps.Children.Move((uint)index, (uint)i);
            }
        }
        _applying = true;
        foreach (var app in _apps)
            _rows[app.Id].Switch.IsOn = _preferences.ShowsNotificationsFrom(app.Id);
        _applying = false;
        Empty.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IEnumerable<(string Id, string Name)> Groups()
    {
        foreach (var group in _listener.Groups)
        {
            if (group.AppId.Length > 0)
                yield return (group.AppId, group.AppName);
        }
    }

    private IEnumerable<(string Id, string Name)> Hidden()
    {
        foreach (var app in _preferences.HiddenApps)
        {
            if (app.TryGetValue("id", out var id) && id is string text && text.Length > 0)
            {
                var name = app.TryGetValue("name", out var value) && value is string named ? named : "";
                yield return (text, name);
            }
        }
    }
}
