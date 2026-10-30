namespace Winshell.Platform.Windows.Tests;

public class FramePaceTests
{
    [Theory]
    [InlineData(true, 0u, true)]
    [InlineData(true, 0xFFFFFFFFu, true)]
    [InlineData(false, 1u, true)]
    [InlineData(false, 16u, true)]
    [InlineData(false, 17u, true)]
    [InlineData(false, 20u, true)]
    [InlineData(false, 21u, false)]
    [InlineData(false, 0u, false)]
    [InlineData(false, 0xFFFFFFFFu, false)]
    [InlineData(false, 60_000u, false)]
    public void A_short_wait_or_a_pending_frame_uses_the_fine_timer(bool framePending, uint timeout, bool fine) =>
        Assert.Equal(fine, FramePace.NeedsFineTimer(framePending, timeout));
}
