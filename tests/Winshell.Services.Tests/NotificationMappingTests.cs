using Winshell.Notifications;

namespace Winshell.Services.Tests;

public sealed class NotificationMappingTests
{
    [Theory]
    [InlineData(0, NotificationMode.Unrestricted)]
    [InlineData(1, NotificationMode.PriorityOnly)]
    [InlineData(2, NotificationMode.AlarmsOnly)]
    [InlineData(19, NotificationMode.Unknown)]
    public void Native_notification_mode_mapping_preserves_unknown_values(int native, NotificationMode expected)
    {
        Assert.Equal(expected, WindowsNotificationBackend.MapMode(native));
    }
}
