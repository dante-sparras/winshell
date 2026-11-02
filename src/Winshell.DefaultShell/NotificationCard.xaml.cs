using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell.Taskbar;
using Winshell.Notifications;

namespace Winshell.DefaultShell;

/// <summary>One notification: its title, its text, when it arrived, and a button that dismisses it.</summary>
public sealed partial class NotificationCard : UserControl
{
    private Notification? _item;
    private NotificationCenter? _center;

    public NotificationCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Notification? Item
    {
        get => _item;
        set
        {
            if (ReferenceEquals(_item, value))
                return;
            Follow(false);
            _item = value;
            Follow(true);
            Apply();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true);
        if (_center is not null)
            _center.PropertyChanged -= OnCenter;
        _center = FindCenter();
        if (_center is not null)
            _center.PropertyChanged += OnCenter;
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Follow(false);
        if (_center is not null)
            _center.PropertyChanged -= OnCenter;
    }

    private void OnItem(object? sender, PropertyChangedEventArgs e) => Apply();

    private void OnCenter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationCenter.Clock))
            Apply();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => _item?.Dismiss();

    private void Apply()
    {
        if (_item is not { } item)
            return;
        TitleText.Text = item.Title;
        BodyText.Text = item.Body;
        AutomationProperties.SetName(DismissButton, NotificationCenter.DismissName(item.Title));
        if (_center is not null)
            TimeText.Text = _center.NotificationTime(item.CreatedAt);
    }

    private NotificationCenter? FindCenter()
    {
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is CalendarFlyoutView flyout)
                return flyout.Center;
        }
        return null;
    }

    private void Follow(bool on)
    {
        if (_item is null)
            return;
        _item.PropertyChanged -= OnItem;
        if (on)
            _item.PropertyChanged += OnItem;
    }
}
