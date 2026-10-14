using FluentAssertions;

namespace Winshell.Tests;

[TestClass]
public sealed class ToastSparesTests
{
    [TestMethod]
    public void Spares_wait_out_the_quiet_minute()
    {
        ToastSpares.ToClose(3, TimeSpan.FromSeconds(59)).Should().Be(0);
    }

    [TestMethod]
    public void After_a_quiet_minute_one_spare_stays()
    {
        ToastSpares.ToClose(3, ToastSpares.Quiet).Should().Be(2);
        ToastSpares.ToClose(1, TimeSpan.FromMinutes(5)).Should().Be(0);
        ToastSpares.ToClose(0, TimeSpan.FromMinutes(5)).Should().Be(0);
    }

    [TestMethod]
    public void One_spare_stays_for_each_monitor_since_a_toast_is_a_window_on_each()
    {
        ToastSpares.ToClose(6, ToastSpares.Quiet, monitors: 2).Should().Be(4);
        ToastSpares.ToClose(2, ToastSpares.Quiet, monitors: 2).Should().Be(0);
        ToastSpares.ToClose(3, ToastSpares.Quiet, monitors: 0).Should().Be(2, "a desktop is never without a monitor");
    }
}
