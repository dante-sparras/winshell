using Jint;
using System.Text;
using System.Text.RegularExpressions;

namespace Winshell.Runtime;

/// <summary>
/// The preprocessor: <c>//@ if hasVersion(0, 3)</c> ... <c>//@ elif</c> ... <c>//@ else</c> ...
/// <c>//@ endif</c>. Lines in a branch that does not apply are blanked, so line numbers stay the same.
/// <c>hasVersion</c> checks the Winshell release stamped into this build.
/// </summary>
public static partial class Preprocessor
{
    /// <summary>The Winshell release <c>hasVersion</c> checks, from the assembly informational version.</summary>
    public static (int Major, int Minor) Version => (ProductVersion.Current.Major, ProductVersion.Current.Minor);

    /// <summary>The Qt version Winshell behaves as.</summary>
    public static readonly (int Major, int Minor) QtVersion = (6, 10);

    public static string Run(string text)
    {
        if (!text.Contains("//@", StringComparison.Ordinal))
            return text;
        var lines = text.Split('\n');
        var output = new StringBuilder(text.Length);
        // Each level: whether this branch is on, and whether an earlier branch was taken.
        var stack = new Stack<(bool Active, bool Taken, bool ParentActive)>();
        var active = true;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//@", StringComparison.Ordinal))
            {
                var directive = trimmed[3..].Trim();
                if (directive.StartsWith("if ", StringComparison.Ordinal))
                {
                    var on = active && Evaluate(directive[3..]);
                    stack.Push((on, on, active));
                    active = on;
                }
                else if (directive.StartsWith("elif ", StringComparison.Ordinal) && stack.Count > 0)
                {
                    var (_, taken, parent) = stack.Pop();
                    var on = parent && !taken && Evaluate(directive[5..]);
                    stack.Push((on, taken || on, parent));
                    active = on;
                }
                else if (directive == "else" && stack.Count > 0)
                {
                    var (_, taken, parent) = stack.Pop();
                    var on = parent && !taken;
                    stack.Push((on, true, parent));
                    active = on;
                }
                else if (directive == "endif" && stack.Count > 0)
                {
                    active = stack.Pop().ParentActive;
                }
                output.Append(line).Append('\n');
                continue;
            }
            output.Append(active ? line : "").Append('\n');
        }
        if (output.Length > 0 && !text.EndsWith('\n'))
            output.Length--;
        return output.ToString();
    }

    private static bool Evaluate(string expression)
    {
        var e = expression.Trim();
        // Expressions combine hasVersion and hasQtVersion calls with && || !.
        e = HasVersion().Replace(e, m =>
        {
            var major = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var minor = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            var features = FeatureNames(m.Groups[4].Value);
            bool ok;
            if (m.Groups[1].Value == "hasQtVersion")
            {
                // hasQtVersion has no feature list. A list keeps the exact language level from matching.
                ok = QtVersion.Major > major || (QtVersion.Major == major && QtVersion.Minor > minor) || (QtVersion == (major, minor) && features.Count == 0);
            }
            else
            {
                ok = ProductVersion.Current.Satisfies(major, minor, features);
            }
            return ok ? "true" : "false";
        });
        try
        {
            var engine = new Jint.Engine();
            return engine.Evaluate(e).AsBoolean();
        }
        catch (Exception ex) when (ex is Jint.Runtime.JavaScriptException or Acornima.ParseErrorException or InvalidOperationException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"(hasVersion|hasQtVersion)\(\s*(\d+)\s*,\s*(\d+)\s*(,\s*\[[^\]]*\])?\s*\)")]
    private static partial Regex HasVersion();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex FeatureName();

    private static List<string> FeatureNames(string list)
    {
        if (!list.Contains('"', StringComparison.Ordinal))
            return [];
        var names = new List<string>();
        foreach (Match name in FeatureName().Matches(list))
            names.Add(name.Groups[1].Value);
        return names;
    }
}
