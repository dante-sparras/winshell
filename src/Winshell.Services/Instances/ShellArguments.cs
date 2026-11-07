using System.Globalization;

namespace Winshell;

/// <summary>
/// What a configuration's program takes from its own command line. Winshell's words are these, and a <c>--</c> ends
/// them, so anything else on the line is the program's. <c>--after</c> is for this start only: a reload names the
/// process it replaces instead. <c>-d</c> is for this start only too: the process that takes over does not detach again.
/// </summary>
internal sealed record ShellArguments(
    int? After,
    bool NoDuplicate,
    bool Daemonize,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Detached,
    bool Verbose)
{
    public static ShellArguments Parse(IEnumerable<string> args)
    {
        int? after = null;
        var noDuplicate = false;
        var daemonize = false;
        var verbose = false;
        var kept = new List<string>();
        var detached = new List<string>();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == CommandWords.End)
            {
                kept.AddRange(list.Skip(i));
                detached.AddRange(list.Skip(i));
                break;
            }

            if (CommandWords.After.Is(list[i]) && i + 1 < list.Count && int.TryParse(list[i + 1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var pid))
            {
                after = pid;
                detached.Add(list[i]);
                detached.Add(list[i + 1]);
                i++;
                continue;
            }

            if (CommandWords.Daemonize.Is(list[i]))
            {
                daemonize = true;
                continue;
            }

            noDuplicate |= CommandWords.NoDuplicate.Is(list[i]);
            verbose |= CommandWords.Verbose.Is(list[i]);
            kept.Add(list[i]);
            detached.Add(list[i]);
        }

        return new ShellArguments(after, noDuplicate, daemonize, kept, detached, verbose);
    }

    /// <summary>
    /// The words <c>winshell run</c> gives a configuration's program. <paramref name="rest"/> is the <c>--</c> and the
    /// words after it.
    /// </summary>
    public static IReadOnlyList<string> Write(bool noDuplicate, bool verbose, IEnumerable<string> rest)
    {
        var words = new List<string>();
        if (noDuplicate)
            words.Add(CommandWords.NoDuplicate.Long);
        if (verbose)
            words.Add(CommandWords.Verbose.Long);
        words.AddRange(rest);
        return words;
    }

    /// <summary>The arguments of the process that takes over from <paramref name="pid"/>. <c>--after</c> comes first, before any <c>--</c>.</summary>
    public IReadOnlyList<string> ForNext(int pid)
    {
        return [CommandWords.After.Long, pid.ToString(CultureInfo.InvariantCulture), .. Kept];
    }
}
