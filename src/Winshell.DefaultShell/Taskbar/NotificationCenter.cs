using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Winshell.Notifications;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// The notifications every taskbar's clock opens. Hidden apps stay out of the list, the count and Clear all.
/// Reading it never asks Windows for access.
/// </summary>
public sealed class NotificationCenter : ObservableObject
{
    private readonly NotificationListener _listener;
    private readonly Preferences _preferences;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private readonly HashSet<NotificationGroup> _watched = [];
    private int _count;
    private string _statusLine = "";
    private string _error = "";
    private string _clock = "";
    private bool _showAllow;
    private bool _showWaiting;
    private bool _showError;
    private bool _showCaughtUp;
    private bool _canClear;

    public NotificationCenter(NotificationListener listener, Preferences preferences, TimeProvider time, CultureInfo? culture = null)
    {
        _listener = listener;
        _preferences = preferences;
        _time = time;
        _culture = culture ?? CultureInfo.CurrentCulture;
        listener.PropertyChanged += OnListener;
        listener.Groups.CollectionChanged += OnGroups;
        preferences.PropertyChanged += OnPreferences;
        foreach (var group in listener.Groups)
            Watch(group);
        Publish();
    }

    public string AllowText => "Allow notification access";

    public string WaitingText => "Waiting for Windows permission\u2026";

    public string CaughtUpText => "You're all caught up";

    /// <summary>The apps the center lists, newest first. A hidden app is not here.</summary>
    public ObservableCollection<NotificationGroup> VisibleGroups { get; } = [];

    /// <summary>How many notifications <see cref="VisibleGroups"/> holds. The clock says this number.</summary>
    public int Count => _count;

    public string StatusLine => _statusLine;

    public string Error => _error;

    /// <summary>The local minute. A card reads it so a time can change when the day does.</summary>
    public string Clock => _clock;

    public bool ShowAllow => _showAllow;

    public bool ShowWaiting => _showWaiting;

    public bool ShowError => _showError;

    public bool ShowCaughtUp => _showCaughtUp;

    public bool CanClear => _canClear;

    /// <summary>The name on a group's header. An app that sent no name is "Unknown app".</summary>
    public static string AppName(string appName) => appName.Length > 0 ? appName : "Unknown app";

    public static string ClearName(string appName) => "Clear notifications from " + appName;

    public static string DismissName(string title) => "Dismiss " + title;

    /// <summary>The short time when the notification is from the local day, otherwise the weekday and that time.</summary>
    public string NotificationTime(DateTimeOffset createdAt)
    {
        var created = TimeZoneInfo.ConvertTime(createdAt, _time.LocalTimeZone);
        var time = created.ToString("t", _culture);
        if (created.Date == _time.GetLocalNow().Date)
            return time;
        return created.ToString("ddd", _culture) + " " + time;
    }

    /// <summary>Dismisses every visible group. A hidden app's notifications stay.</summary>
    public void ClearAll()
    {
        foreach (var group in VisibleGroups.ToArray())
            group.Dismiss();
    }

    /// <summary>The Allow button. Nothing else asks Windows for access.</summary>
    public void RequestAccess() => _listener.RequestAccess();

    public void NoteClock()
    {
        var key = _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Set(ref _clock, key, nameof(Clock));
    }

    private void OnListener(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationListener.AccessStatus) or nameof(NotificationListener.Error)
            or nameof(NotificationListener.DoNotDisturbState) or nameof(NotificationListener.FocusState))
            Publish();
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Preferences.HiddenNotificationApps))
            Publish();
    }

    private void OnGroups(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                {
                    foreach (var group in e.NewItems.OfType<NotificationGroup>())
                        Watch(group);
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                {
                    foreach (var group in e.OldItems.OfType<NotificationGroup>())
                        Unwatch(group);
                }
                break;
            default:
                foreach (var group in _watched.ToArray())
                    Unwatch(group);
                foreach (var group in _listener.Groups)
                    Watch(group);
                break;
        }
        Publish();
    }

    private void OnGroupNotifications(object? sender, NotifyCollectionChangedEventArgs e) => Publish();

    private void Watch(NotificationGroup group)
    {
        if (!_watched.Add(group))
            return;
        group.Notifications.CollectionChanged += OnGroupNotifications;
    }

    private void Unwatch(NotificationGroup group)
    {
        if (!_watched.Remove(group))
            return;
        group.Notifications.CollectionChanged -= OnGroupNotifications;
    }

    private void Publish()
    {
        List<NotificationGroup> next = [];
        foreach (var group in _listener.Groups)
        {
            if (_preferences.ShowsNotificationsFrom(group.AppId))
                next.Add(group);
        }
        if (!Same(next))
        {
            VisibleGroups.Clear();
            foreach (var group in next)
                VisibleGroups.Add(group);
        }

        var count = 0;
        foreach (var group in VisibleGroups)
            count += group.Notifications.Count;
        Set(ref _count, count, nameof(Count));
        Set(ref _statusLine, "Do not disturb: " + Word(_listener.DoNotDisturbState) + " \u00b7 Focus: " + Word(_listener.FocusState), nameof(StatusLine));
        Set(ref _error, _listener.Error, nameof(Error));
        Set(ref _showAllow, _listener.AccessStatus == NotificationAccessStatus.Unspecified, nameof(ShowAllow));
        Set(ref _showWaiting, _listener.AccessStatus == NotificationAccessStatus.Requesting, nameof(ShowWaiting));
        Set(ref _showError, _listener.Error.Length > 0, nameof(ShowError));
        Set(ref _showCaughtUp, _listener.AccessStatus == NotificationAccessStatus.Allowed && VisibleGroups.Count == 0, nameof(ShowCaughtUp));
        Set(ref _canClear, VisibleGroups.Count > 0, nameof(CanClear));
    }

    private bool Same(List<NotificationGroup> next)
    {
        if (VisibleGroups.Count != next.Count)
            return false;
        for (var i = 0; i < next.Count; i++)
        {
            if (!ReferenceEquals(VisibleGroups[i], next[i]))
                return false;
        }
        return true;
    }

    // On and Off are named. Anything else, including Unknown, reads as Unknown, as the QML does.
    private static string Word(NotificationState state) => state switch
    {
        NotificationState.On => "On",
        NotificationState.Off => "Off",
        _ => "Unknown",
    };
}
