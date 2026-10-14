namespace Winshell.Core;

/// <summary>What to write about one failure.</summary>
/// <param name="First">True for the failure that starts a run. It is written in full, with its stack.</param>
/// <param name="Count">The failures in the run so far when one count line is due. Zero when nothing is written.</param>
public readonly record struct FaultNote(bool First, int Count);

/// <summary>
/// Counts the failures of a thing that is tried again every turn, such as a plugin's tick (ADR 0041), so the log gets the
/// first one in full and then a count now and then. Written out each time, a plugin that throws on every turn fills both
/// chunks of the log within minutes and pushes out the lines around its first failure. Not thread safe.
/// </summary>
public sealed class FaultRuns
{
    /// <summary>How long a run goes on between two count lines, in milliseconds.</summary>
    public const long CountEveryMs = 600_000;

    private readonly Dictionary<string, Run> _runs = new(StringComparer.Ordinal);

    /// <summary>Notes that <paramref name="id"/> failed.</summary>
    /// <param name="id">What failed.</param>
    /// <param name="now">Milliseconds from a steady clock.</param>
    /// <returns>What to write for it.</returns>
    public FaultNote Failed(string id, long now)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_runs.TryGetValue(id, out var run))
        {
            _runs[id] = new Run(1, now);
            return new FaultNote(First: true, Count: 1);
        }

        var count = run.Count + 1;
        if (now - run.WrittenAt < CountEveryMs)
        {
            _runs[id] = run with { Count = count };
            return default;
        }

        _runs[id] = new Run(count, now);
        return new FaultNote(First: false, Count: count);
    }

    /// <summary>Notes that <paramref name="id"/> worked, which ends its run.</summary>
    /// <param name="id">What worked.</param>
    /// <returns>The failures the run had. Zero when it was not failing.</returns>
    public int Recovered(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _runs.Remove(id, out var run) ? run.Count : 0;
    }

    private readonly record struct Run(int Count, long WrittenAt);
}
