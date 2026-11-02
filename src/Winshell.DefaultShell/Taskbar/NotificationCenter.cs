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
            group.Notifications.CollectionChanged += OnNotifications;
        Publish();
    }

    public string AllowText => "Allow notification access";

    public string WaitingText => "Waiting for Windows permission\u2026";

    public string CaughtUpText => "You're all caught up";

    /// <summary>The apps the center lists, newest first. A hidden app is not here.</summary>
    public ObservableCollection<NotificationGroup> VisibleGroups { get; } = [];

    /// <summary>How many notifications <see cref="VisibleGroups"/> holds. The clock says this number.</summary>
    public int Count { get; private set => Set(ref field, value); }

    public string StatusLine { get; private set => Set(ref field, value); } = "";

    public string Error { get; private set => Set(ref field, value); } = "";

    /// <summary>The local minute. A card reads it so a time can change when the day does.</summary>
    public string Clock { get; private set => Set(ref field, value); } = "";

    public bool ShowAllow { get; private set => Set(ref field, value); }

    public bool ShowWaiting { get; private set => Set(ref field, value); }

    public bool ShowError { get; private set => Set(ref field, value); }

    public bool ShowCaughtUp { get; private set => Set(ref field, value); }

    public bool CanClear { get; private set => Set(ref field, value); }

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

    public void NoteClock() => Clock = _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

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
        foreach (var group in e.NewItems?.OfType<NotificationGroup>() ?? [])
            group.Notifications.CollectionChanged += OnNotifications;
        foreach (var group in e.OldItems?.OfType<NotificationGroup>() ?? [])
            group.Notifications.CollectionChanged -= OnNotifications;
        Publish();
    }

    private void OnNotifications(object? sender, NotifyCollectionChangedEventArgs e) => Publish();

    private void Publish()
    {
        List<NotificationGroup> next = [.. _listener.Groups.Where(group => _preferences.ShowsNotificationsFrom(group.AppId))];
        if (!VisibleGroups.SequenceEqual(next))
        {
            VisibleGroups.Clear();
            foreach (var group in next)
                VisibleGroups.Add(group);
        }
        Count = next.Sum(group => group.Notifications.Count);
        StatusLine = "Do not disturb: " + Word(_listener.DoNotDisturbState) + " \u00b7 Focus: " + Word(_listener.FocusState);
        Error = _listener.Error;
        ShowAllow = _listener.AccessStatus == NotificationAccessStatus.Unspecified;
        ShowWaiting = _listener.AccessStatus == NotificationAccessStatus.Requesting;
        ShowError = _listener.Error.Length > 0;
        ShowCaughtUp = _listener.AccessStatus == NotificationAccessStatus.Allowed && next.Count == 0;
        CanClear = next.Count > 0;
    }

    // On and Off are named. Anything else, including Unknown, reads as Unknown.
    private static string Word(NotificationState state) => state switch
    {
        NotificationState.On => "On",
        NotificationState.Off => "Off",
        _ => "Unknown",
    };
}
