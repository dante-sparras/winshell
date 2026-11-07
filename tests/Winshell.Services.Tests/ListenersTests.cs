namespace Winshell.Services.Tests;

/// <summary>
/// A shared backend's handler that throws is reported to the service that added it, once, and does not stop the next
/// one.
/// </summary>
public class ListenersTests
{
    [Fact]
    public void A_handler_that_throws_goes_to_its_own_reporter_and_the_next_still_runs()
    {
        var listeners = new Listeners<Action<int>>();
        var failure = new InvalidOperationException("listener failed");
        List<Exception> first = [];
        List<Exception> second = [];
        var later = 0;
        using var throwing = listeners.Add(_ => throw failure, first.Add);
        using var counting = listeners.Add(value => later = value, second.Add);

        listeners.Raise(handler => handler(7));

        Assert.Same(failure, Assert.Single(first));
        Assert.Empty(second);
        Assert.Equal(7, later);
    }

    [Fact]
    public void A_removed_handler_is_not_called()
    {
        var listeners = new Listeners<Action>();
        var calls = 0;
        var added = listeners.Add(() => calls++, e => Assert.Fail(e.Message));

        added.Dispose();
        listeners.Raise(handler => handler());

        Assert.Equal(0, calls);
    }
}
