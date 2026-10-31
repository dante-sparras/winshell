using Winshell.Platform.Windows.Services.Power;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Holds real power requests for an instant and always clears them. Which requests Windows lists needs admin to read, so these check what each <c>KeepAwake</c> holds.
/// </summary>
public sealed class KeepAwakeTests
{
    [Fact]
    public void Enabling_holds_a_power_request_and_disabling_clears_it()
    {
        var awake = new KeepAwake();
        try
        {
            Assert.False(awake.HoldsRequest);
            awake.Enabled = true;
            Assert.True(awake.HoldsRequest);
            Assert.True(KeepAwake.AnyActive);
            awake.Enabled = false;
            Assert.False(awake.HoldsRequest);
        }
        finally
        {
            awake.Destroy();
        }
    }

    [Fact]
    public void Two_instances_hold_their_own_requests()
    {
        var first = new KeepAwake();
        var second = new KeepAwake();
        try
        {
            first.Enabled = true;
            second.Enabled = true;
            Assert.True(first.HoldsRequest);
            Assert.True(second.HoldsRequest);

            first.Enabled = false;
            Assert.False(first.HoldsRequest);
            Assert.True(second.HoldsRequest);
            Assert.True(KeepAwake.AnyActive);
        }
        finally
        {
            first.Destroy();
            second.Destroy();
        }
    }

    [Fact]
    public void Destroying_an_enabled_instance_clears_its_request()
    {
        var awake = new KeepAwake { Enabled = true };
        Assert.True(awake.HoldsRequest);
        awake.Destroy();
        Assert.False(awake.HoldsRequest);
    }
}
