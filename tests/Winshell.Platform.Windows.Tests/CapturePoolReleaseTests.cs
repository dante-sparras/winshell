using System.Runtime.InteropServices;
using Winshell.Platform.Windows.Services.Capture;

namespace Winshell.Platform.Windows.Tests;

public class CapturePoolReleaseTests
{
    [Fact]
    public void Releasing_a_pool_stops_the_session_and_drops_its_frames_first()
    {
        var order = new List<string>();
        var frames = new Queue<RecordingDisposable>();
        frames.Enqueue(new RecordingDisposable("frame", order));
        WindowsCaptureBackend.ReleaseSessionAndPool(
            new RecordingDisposable("session", order),
            new RecordingDisposable("pool", order),
            () => frames.Count == 0 ? null : frames.Dequeue());

        Assert.Equal(["session", "frame", "pool"], order);
    }

    [Fact]
    public void A_session_teardown_error_still_releases_the_pool()
    {
        var order = new List<string>();
        WindowsCaptureBackend.ReleaseSessionAndPool(
            new ThrowingDisposable(),
            new RecordingDisposable("pool", order),
            () => null);

        Assert.Equal(["pool"], order);
    }

    private sealed class RecordingDisposable(string name, List<string> order) : IDisposable
    {
        public void Dispose() => order.Add(name);
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new COMException("session already closed");
    }
}
