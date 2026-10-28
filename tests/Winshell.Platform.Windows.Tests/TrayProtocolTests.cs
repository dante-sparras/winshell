using Winshell.Platform.Windows.Services.Tray;

namespace Winshell.Platform.Windows.Tests;

public class TrayProtocolTests
{
    [Fact]
    public void Version_4_callbacks_carry_the_point_and_the_icon_id()
    {
        var (w, l) = TrayProtocol.Callback(TrayProtocol.Version4, 7, 0x0205, 20, 40);
        Assert.Equal((nint)(20u | (40u << 16)), w);
        Assert.Equal((nint)(0x0205u | (7u << 16)), l);
    }

    [Fact]
    public void Older_callbacks_carry_the_icon_id_and_the_message()
    {
        var (w, l) = TrayProtocol.Callback(TrayProtocol.Version, 7, 0x0205, 20, 40);
        Assert.Equal((nint)7, w);
        Assert.Equal((nint)0x0205, l);
    }

    [Fact]
    public void A_point_left_of_the_origin_keeps_its_low_16_bits()
    {
        var (w, _) = TrayProtocol.Callback(TrayProtocol.Version4, 1, 0x007B, -8, 100);
        Assert.Equal((nint)(0xfff8u | (100u << 16)), w);
    }
}
