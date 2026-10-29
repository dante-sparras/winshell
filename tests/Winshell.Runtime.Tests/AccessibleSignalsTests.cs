using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A notification item is registered with its scene, including when the role is set before it has a parent.</summary>
public class AccessibleSignalsTests
{
    [Fact]
    public void A_notification_is_registered_and_an_edit_is_dirty()
    {
        var scene = new Scene();
        var early = new Item();
        var earlyAccessible = early.GetAttached(o => new AccessibleAttached(o));
        earlyAccessible.Role = (int)AccessibleAttached.RoleValue.Notification;
        early.SetParentItem(scene.Root);
        Assert.Contains(early, scene.Signals.Notifications);

        var item = new Item();
        item.SetParentItem(scene.Root);
        var accessible = item.GetAttached(o => new AccessibleAttached(o));
        accessible.Role = (int)AccessibleAttached.RoleValue.Notification;
        Assert.Contains(item, scene.Signals.Notifications);
        accessible.Role = (int)AccessibleAttached.RoleValue.Button;
        Assert.DoesNotContain(item, scene.Signals.Notifications);
        Assert.True(scene.Signals.TakeDirty());
        accessible.Name = "Go";
        Assert.True(scene.Signals.TakeDirty());
        Assert.False(scene.Signals.TakeDirty());

        early.Destroy();
        Assert.DoesNotContain(early, scene.Signals.Notifications);
    }
}
