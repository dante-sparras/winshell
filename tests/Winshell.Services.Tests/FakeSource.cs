using Winshell.Power;

namespace Winshell.Services.Tests;

/// <summary>A Windows state a test sets by hand. A push says it changed, and a read returns the last push.</summary>
internal class FakeSource<T>(T initial)
    where T : class
{
    private int _reads;

    public event Action? Changed;

    public T Current { get; private set; } = initial;

    public int Reads => Volatile.Read(ref _reads);

    public bool Disposed { get; private set; }

    public void Push(T next)
    {
        Current = next;
        Changed?.Invoke();
    }

    public Task<T> ReadAsync(CancellationToken cancellation)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(Current);
    }

    public void Dispose()
    {
        Disposed = true;
    }
}

internal sealed class FakePowerBackend(PowerSnapshot initial) : FakeSource<PowerSnapshot>(initial), IPowerBackend;

internal sealed class FakePowerModeBackend(Func<PowerModeLevel, uint> set)
    : FakeSource<PowerModeSnapshot>(PowerModeSnapshot.Initial), IPowerModeBackend
{
    public uint Set(PowerModeLevel mode)
    {
        return set(mode);
    }
}

internal sealed class FakeBrightnessBackend(BrightnessSnapshot initial, Func<string, int, string?> set)
    : FakeSource<BrightnessSnapshot>(initial), IBrightnessBackend
{
    public string? Set(string deviceName, int brightness)
    {
        return set(deviceName, brightness);
    }
}
