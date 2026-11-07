namespace Winshell.ShellTests;

public sealed class ServiceTeardownTests
{
    [Fact]
    public void A_service_that_throws_on_dispose_is_logged_and_the_rest_still_stop()
    {
        var lines = new List<string>();
        var log = new Log { Sink = (_, category, message) => lines.Add($"{category}: {message}") };
        var stopped = new List<string>();

        Shell.DisposeEach([new Stops("first", stopped), new Throws(), null, new Stops("last", stopped)], log);

        Assert.Equal(["first", "last"], stopped);
        Assert.StartsWith("shell: Throws did not stop: System.InvalidOperationException: Device gone",
            Assert.Single(lines), StringComparison.Ordinal);
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
