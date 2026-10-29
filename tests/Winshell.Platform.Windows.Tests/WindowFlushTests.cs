using Winshell.Platform.Windows.Services.WindowList;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A flush must not drop hwnds that arrive while it is describing a window.</summary>
public class WindowFlushTests
{
    [Fact]
    public void A_mark_during_describe_stays_pending()
    {
        var added = false;
        try
        {
            WindowTracker.MarkDirty(1);
            WindowTracker.FlushPending(() =>
            {
                if (added)
                    return;
                added = true;
                WindowTracker.MarkDirty(99);
            });
            Assert.Equal(1, WindowTracker.PendingCount);
        }
        finally
        {
            WindowTracker.FlushPending(null);
            Assert.Equal(0, WindowTracker.PendingCount);
        }
    }
}
