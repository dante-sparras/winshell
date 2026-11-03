using System.Collections.Immutable;

namespace Winshell;

internal sealed record CliOptions
{
    public string? Command { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public bool ShellStartAllowed { get; init; }

    public ImmutableArray<string> Arguments { get; init; } = [];

    public string? Path { get; init; }

    public string? Config { get; init; }

    public string? Id { get; init; }

    public int? Pid { get; init; }

    /// <summary>A shell that restarted to update: this one waits for that process to end first.</summary>
    public int? After { get; init; }

    public bool Daemonize { get; init; }

    public bool NoDuplicate { get; init; }

    public bool Verbose { get; init; }

    public bool Json { get; init; }

    public bool Follow { get; init; }

    /// <summary>End the process when the instance does not exit after it is asked.</summary>
    public bool Force { get; init; }

    public bool Hard { get; init; }

    /// <summary>Run the program the last build left. A failed build never starts that program unless this is set.</summary>
    public bool NoBuild { get; init; }

    public bool NoColor { get; init; }

    public int? Tail { get; init; }

    public ImmutableArray<string> Positional { get; init; } = [];

    /// <summary>The <c>--</c> and everything after it. Those words belong to the configuration's program.</summary>
    public ImmutableArray<string> Rest { get; init; } = [];
}
