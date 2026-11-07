namespace Winshell.ShellTests;

public sealed class ServiceTeardownTests
{
    [Fact]
    public void A_service_that_throws_on_dispose_lets_the_rest_stop_then_its_exception_is_thrown()
    {
        var stopped = new List<string>();

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            Shell.DisposeEach([new Stops("first", stopped), new Throws(), null, new Stops("last", stopped)]));

        Assert.Equal("Device gone", thrown.Message);
        Assert.Equal(["first", "last"], stopped);
    }

    private sealed class Stops(string name, List<string> stopped) : IDisposable
    {
        public void Dispose()
        {
            stopped.Add(name);
        }
    }

    private sealed class Throws : IDisposable
    {
        public void Dispose()
        {
            throw new InvalidOperationException("Device gone");
        }
    }
}
