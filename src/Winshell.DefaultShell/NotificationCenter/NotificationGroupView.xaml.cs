using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;
using Winshell.Notifications;

namespace Winshell.DefaultShell;

/// <summary>One app's notifications, and the button that clears that app.</summary>
public sealed partial class NotificationGroupView : UserControl
{
    private NotificationGroup? _group;

    public NotificationGroupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public NotificationGroup? Group
    {
        get => _group;
        set
        {
            if (ReferenceEquals(_group, value))
                return;
            Follow(false);
            _group = value;
            Follow(true);
            Cards.ItemsSource = _group?.Notifications;
            Apply();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true);
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Follow(false);
    }

    private void OnGroup(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationGroup.AppName))
            Apply();
    }

    private void OnClear(object sender, RoutedEventArgs e) => _group?.Dismiss();

    private void Apply()
    {
        if (_group is not { } group)
            return;
        TitleText.Text = NotificationCenter.AppName(group.AppName);
        AutomationProperties.SetName(ClearButton, NotificationCenter.ClearName(group.AppName));
    }

    private void Follow(bool on)
    {
        if (_group is null)
            return;
        _group.PropertyChanged -= OnGroup;
        if (on)
            _group.PropertyChanged += OnGroup;
    }
}
