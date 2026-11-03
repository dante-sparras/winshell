using System.Diagnostics;
using System.Globalization;

namespace Winshell;

/// <summary>
/// What a configuration's program takes from its own command line. Winshell's words are these, and a <c>--</c> ends
/// them, so anything else on the line is the program's. <c>--after</c> is for this start only: a reload names the
/// process it replaces instead. <c>-d</c> is for this start only too: the process that takes over does not detach again.
/// </summary>
internal sealed record ShellArguments(int? After, bool NoDuplicate, bool Daemonize, IReadOnlyList<string> Kept, IReadOnlyList<string> Detached, bool Verbose)
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
            if (list[i] == "--")
            {
                kept.AddRange(list.Skip(i));
                detached.AddRange(list.Skip(i));
                break;
            }
            if (list[i] == "--after" && i + 1 < list.Count && int.TryParse(list[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                after = pid;
                detached.Add(list[i]);
                detached.Add(list[i + 1]);
                i++;
                continue;
            }
            if (list[i] is "-d" or "--daemonize")
            {
                daemonize = true;
                continue;
            }
            noDuplicate |= list[i] is "-n" or "--no-duplicate";
            verbose |= list[i] is "-v" or "--verbose";
            kept.Add(list[i]);
            detached.Add(list[i]);
        }
        return new ShellArguments(after, noDuplicate, daemonize, kept, detached, verbose);
    }

    /// <summary>The arguments of the process that takes over from <paramref name="pid"/>. <c>--after</c> comes first, before any <c>--</c>.</summary>
    public IReadOnlyList<string> ForNext(int pid) => ["--after", pid.ToString(CultureInfo.InvariantCulture), .. Kept];

    /// <summary>
    /// The process that takes a <c>-d</c> start over. <c>CreateNoWindow</c> is what <c>winshell -d</c> uses. The window
    /// style stays normal: a window's first show takes the state its launcher asked for, and hiding it here would hide
    /// the shell.
    /// </summary>
    public ProcessStartInfo DetachedStart(string program)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in Detached)
            info.ArgumentList.Add(argument);
        return info;
    }
}
