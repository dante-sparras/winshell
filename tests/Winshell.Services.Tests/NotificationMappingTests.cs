using Windows.UI.Notifications.Management;
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

    [Theory]
    [InlineData(UserNotificationListenerAccessStatus.Allowed, NotificationAccessStatus.Allowed)]
    [InlineData(UserNotificationListenerAccessStatus.Denied, NotificationAccessStatus.Denied)]
    [InlineData(UserNotificationListenerAccessStatus.Unspecified, NotificationAccessStatus.Unspecified)]
    public void Native_access_mapping_preserves_prompt_results(UserNotificationListenerAccessStatus native,
        NotificationAccessStatus expected)
    {
        Assert.Equal(expected, NotificationListenerAccess.Map(native));
    }

    [Fact]
    public async Task Native_read_is_safe_without_prompting_or_changing_settings()
    {
        var snapshot = await new WindowsNotificationBackend().ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(Enum.IsDefined(snapshot.AccessStatus));
        Assert.True(Enum.IsDefined(snapshot.Focus));
        Assert.True(Enum.IsDefined(snapshot.Mode));
        if (snapshot.AccessStatus != NotificationAccessStatus.Allowed)
            Assert.Empty(snapshot.Notifications);
    }
}
