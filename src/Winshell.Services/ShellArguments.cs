using System.Globalization;

namespace Winshell;

/// <summary>
/// What a configuration's program takes from its own command line. Winshell's words are these two, and a <c>--</c> ends
/// them, so anything else on the line is the program's. <c>--after</c> is for this start only: a reload names the
/// process it replaces instead.
/// </summary>
internal sealed record ShellArguments(int? After, bool NoDuplicate, IReadOnlyList<string> Kept)
{
    public static ShellArguments Parse(IEnumerable<string> args)
    {
        int? after = null;
        var noDuplicate = false;
        var kept = new List<string>();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == "--")
            {
                kept.AddRange(list.Skip(i));
                break;
            }
            if (list[i] == "--after" && i + 1 < list.Count && int.TryParse(list[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                after = pid;
                i++;
                continue;
            }
            noDuplicate |= list[i] is "-n" or "--no-duplicate";
            kept.Add(list[i]);
        }
        return new ShellArguments(after, noDuplicate, kept);
    }

    /// <summary>The arguments of the process that takes over from <paramref name="pid"/>. <c>--after</c> comes first, before any <c>--</c>.</summary>
    public IReadOnlyList<string> ForNext(int pid) => ["--after", pid.ToString(CultureInfo.InvariantCulture), .. Kept];
}
