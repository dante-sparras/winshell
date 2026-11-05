using Windows.UI.Notifications.Management;

namespace Winshell.Notifications;

/// <summary>
/// Whether Windows lets this process read notifications. <see cref="WindowsNotificationBackend"/> maps every answer
/// through <see cref="Map"/>, and the doctor calls <see cref="Read"/>, which needs no shell services.
/// </summary>
internal static class NotificationListenerAccess
{
    /// <summary>Asks Windows without a prompt. Unavailable when Windows cannot answer, as without package identity.</summary>
    public static NotificationAccessStatus Read()
    {
        try
        {
            return Map(UserNotificationListener.Current.GetAccessStatus());
        }
        catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
        {
            return NotificationAccessStatus.Unavailable;
        }
    }

    public static NotificationAccessStatus Map(UserNotificationListenerAccessStatus access) => access switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccessStatus.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccessStatus.Denied,
        _ => NotificationAccessStatus.Unspecified,
    };
}
