using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>A layout that never settles is reported instead of being dropped quietly.</summary>
public class LayoutLoopTests
{
    [Fact]
    public void A_layout_that_never_settles_is_logged()
    {
        var saved = Log.Default.Sink;
        var messages = new List<string>();
        Log.Default.Sink = (_, _, message) => messages.Add(message);
        try
        {
            var loop = new ManualEventLoop();
            var item = new LoopingPolish();
            PolishQueue.Request(item, loop);
            loop.Process();
            Assert.Contains(messages, message => message.Contains("did not settle", StringComparison.Ordinal));
            Assert.Equal(50, item.Polished);
        }
        finally
        {
            Log.Default.Sink = saved;
        }
    }

    private sealed class LoopingPolish : IPolishable
    {
        public int Polished { get; private set; }

        public void UpdatePolish()
        {
            Polished++;
            PolishQueue.Request(this, null);
        }
    }
}
