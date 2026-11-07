using Winshell.Windows;

namespace Winshell.Services.Tests;

public sealed class VirtualDesktopRulesTests
{
    [Theory]
    [InlineData(22631u, false)]
    [InlineData(26099u, false)]
    [InlineData(26100u, true)]
    [InlineData(26200u, true)]
    [InlineData(26300u, true)]
    [InlineData(26399u, true)]
    [InlineData(26400u, false)]
    [InlineData(28000u, false)]
    public void Only_builds_26100_through_26399_use_the_desktop_method_table(uint build, bool supported)
    {
        var contract = DesktopContract.ForBuild(build);
        Assert.Equal(supported, contract.Supported);
        if (!supported)
            Assert.Contains($"{build}", contract.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_desktop_name_becomes_Desktop_N_and_a_stored_name_is_kept()
    {
        Assert.Equal("Desktop 1", VirtualDesktopRules.DisplayName("  ", 0));
        Assert.Equal("Desktop 2", VirtualDesktopRules.DisplayName(null, 1));
        Assert.Equal("Work", VirtualDesktopRules.DisplayName("  Work  ", 3));
        Assert.Equal("", VirtualDesktopRules.FormatId(Guid.Empty));
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff",
            VirtualDesktopRules.FormatId(new Guid("00112233-4455-6677-8899-aabbccddeeff")));
    }

    [Fact]
    public void Removing_a_desktop_falls_back_to_the_previous_one_and_never_the_last()
    {
        Assert.Equal(1, VirtualDesktopRules.FallbackIndex(0, 3));
        Assert.Equal(0, VirtualDesktopRules.FallbackIndex(1, 3));
        Assert.Equal(1, VirtualDesktopRules.FallbackIndex(2, 3));
        Assert.Null(VirtualDesktopRules.FallbackIndex(0, 1));
        Assert.Null(VirtualDesktopRules.FallbackIndex(3, 3));
        Assert.Null(VirtualDesktopRules.FallbackIndex(-1, 2));
    }
}
