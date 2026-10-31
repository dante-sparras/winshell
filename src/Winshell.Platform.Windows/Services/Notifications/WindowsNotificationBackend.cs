using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Windows.UI.Shell;
using Winshell.Platform.Windows.Services.Updates;

namespace Winshell.Platform.Windows.Services.Notifications;

internal interface INotificationBackend
{
    Task<NotificationAccessStatus> RequestAccessAsync();
    Task<NotificationSnapshot> ReadAsync();
    void Dismiss(uint id);
    Action? WatchToasts(Action changed) => null;
}

internal sealed class WindowsNotificationBackend : INotificationBackend
{
    public async Task<NotificationAccessStatus> RequestAccessAsync() =>
        MapAccess(await UserNotificationListener.Current.RequestAccessAsync());

    public async Task<NotificationSnapshot> ReadAsync()
    {
        // Focus and notification restriction mode do not require listener permission.
        var focus = ReadFocus();
        var mode = ReadMode();
        try
        {
            var listener = UserNotificationListener.Current;
            var access = MapAccess(listener.GetAccessStatus());
            if (access != NotificationAccessStatus.Allowed)
                return new(access, [], focus, mode);
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            // Access may be revoked while the asynchronous read is in progress.
            access = MapAccess(listener.GetAccessStatus());
            if (access != NotificationAccessStatus.Allowed)
                return new(access, [], focus, mode);
            List<IncomingToast> toasts = [];
            foreach (var notification in notifications)
            {
                try { toasts.Add(ReadToast(notification)); }
                catch (Exception e) when (IsExpectedFailure(e))
                {
                    // An app can disappear between enumeration and reading its toast.
                    Runtime.Log.Default.Warn("notifications", e.Message);
                }
            }
            return new(access, toasts, focus, mode);
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return new(NotificationAccessStatus.Unavailable, [], focus, mode,
                $"Windows notification access is unavailable: {e.Message} Check package identity and the userNotificationListener capability.");
        }
    }

    public void Dismiss(uint id)
    {
        var listener = UserNotificationListener.Current;
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            throw new UnauthorizedAccessException("Windows notification listener access is no longer allowed.");
        listener.RemoveNotification(id);
    }

    /// <summary>
    /// Calls <paramref name="changed"/> when a toast arrives or goes, and returns the call that stops it. Null without
    /// package identity or access, where Windows does not raise the event.
    /// </summary>
    public Action? WatchToasts(Action changed)
    {
        if (!Packaging.IsPackaged)
            return null;
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
                return null;
            TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs> handler = (_, _) => changed();
            listener.NotificationChanged += handler;
            return () => listener.NotificationChanged -= handler;
        }
        catch (Exception e) when (IsExpectedFailure(e))
        {
            return null;
        }
    }

    internal static NotificationAccessStatus MapAccess(UserNotificationListenerAccessStatus access) => access switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccessStatus.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccessStatus.Denied,
        _ => NotificationAccessStatus.Unspecified,
    };

    internal static IncomingToast ReadToast(UserNotification notification)
    {
        var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)
            ?? notification.Notification.Visual.Bindings.FirstOrDefault();
        var texts = binding?.GetTextElements().Select(t => t.Text).ToArray() ?? [];
        var appId = "";
        var appName = "";
        try
        {
            appId = notification.AppInfo.AppUserModelId;
            appName = notification.AppInfo.DisplayInfo.DisplayName;
        }
        catch (Exception e) when (IsExpectedFailure(e)) { }
        return new(notification.Id, appId, appName, texts.FirstOrDefault() ?? "",
            string.Join('\n', texts.Skip(1)), notification.CreationTime, notification.Notification.ExpirationTime);
    }

    private static NotificationState ReadFocus()
    {
        try
        {
            if (ApiInformation.IsTypePresent("Windows.UI.Shell.FocusSessionManager") && FocusSessionManager.IsSupported)
                return FocusSessionManager.GetDefault().IsFocusActive ? NotificationState.On : NotificationState.Off;
        }
        catch (Exception e) when (IsExpectedFailure(e)) { }
        return NotificationState.Unknown;
    }

    private static NotificationMode ReadMode()
    {
        try
        {
            if (ApiInformation.IsPropertyPresent("Windows.UI.Notifications.ToastNotificationManagerForUser", "NotificationMode"))
                return MapMode((int)ToastNotificationManager.GetDefault().NotificationMode);
        }
        catch (Exception e) when (IsExpectedFailure(e)) { }
        return NotificationMode.Unknown;
    }

    internal static NotificationMode MapMode(int mode) => mode switch
    {
        0 => NotificationMode.Unrestricted,
        1 => NotificationMode.PriorityOnly,
        2 => NotificationMode.AlarmsOnly,
        _ => NotificationMode.Unknown,
    };

    internal static bool IsExpectedFailure(Exception e) => e is COMException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or NotSupportedException or TypeLoadException;
}
