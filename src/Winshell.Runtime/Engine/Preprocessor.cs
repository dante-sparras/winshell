using System.Globalization;
using System.Text;

namespace Winshell.Runtime;

/// <summary>
/// The preprocessor: <c>//@ if hasVersion(0, 3)</c> ... <c>//@ elif</c> ... <c>//@ else</c> ...
/// <c>//@ endif</c>. Lines in a branch that does not apply are blanked, so line numbers stay the same.
/// <c>hasVersion</c> checks the Winshell release stamped into this build.
/// An expression is only <c>hasVersion</c>, <c>hasQtVersion</c>, <c>true</c>, <c>false</c>,
/// <c>!</c>, <c>&amp;&amp;</c>, <c>||</c>, and parentheses. It is not script.
/// </summary>
public static class Preprocessor
{
    /// <summary>The Winshell release <c>hasVersion</c> checks, from the assembly informational version.</summary>
    public static (int Major, int Minor) Version => (ProductVersion.Current.Major, ProductVersion.Current.Minor);

    /// <summary>The Qt version Winshell behaves as.</summary>
    public static readonly (int Major, int Minor) QtVersion = (6, 10);

    public static string Run(string text) => Run(text, null);

    /// <summary>Runs the preprocessor. <paramref name="warnings"/> receives one line per problem, including its line number.</summary>
    public static string Run(string text, ICollection<string>? warnings)
    {
        if (!text.Contains("//@", StringComparison.Ordinal))
            return text;
        var lines = text.Split('\n');
        var output = new StringBuilder(text.Length);
        // Each level: whether this branch is on, whether an earlier branch was taken, and the line of the if.
        var stack = new Stack<(bool Active, bool Taken, bool ParentActive, int Line)>();
        var active = true;
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//@", StringComparison.Ordinal))
            {
                var directive = trimmed[3..].Trim();
                if (directive.StartsWith("if ", StringComparison.Ordinal))
                {
                    var on = active && Evaluate(directive[3..], lineNumber, warnings);
                    stack.Push((on, on, active, lineNumber));
                    active = on;
                }
                else if (directive.StartsWith("elif ", StringComparison.Ordinal))
                {
                    if (stack.Count == 0)
                        warnings?.Add($"line {lineNumber}: elif without a matching if");
                    else
                    {
                        var (_, taken, parent, ifLine) = stack.Pop();
                        var on = parent && !taken && Evaluate(directive[5..], lineNumber, warnings);
                        stack.Push((on, taken || on, parent, ifLine));
                        active = on;
                    }
                }
                else if (directive == "else")
                {
                    if (stack.Count == 0)
                        warnings?.Add($"line {lineNumber}: else without a matching if");
                    else
                    {
                        var (_, taken, parent, ifLine) = stack.Pop();
                        var on = parent && !taken;
                        stack.Push((on, true, parent, ifLine));
                        active = on;
                    }
                }
                else if (directive == "endif")
                {
                    if (stack.Count == 0)
                        warnings?.Add($"line {lineNumber}: endif without a matching if");
                    else
                        active = stack.Pop().ParentActive;
                }
                else
                    warnings?.Add($"line {lineNumber}: unknown preprocessor directive '{directive}'");
                output.Append(line).Append('\n');
                continue;
            }
            output.Append(active ? line : "").Append('\n');
        }
        while (stack.Count > 0)
            warnings?.Add($"line {stack.Pop().Line}: if was not closed");
        if (output.Length > 0 && !text.EndsWith('\n'))
            output.Length--;
        return output.ToString();
    }

    private static bool Evaluate(string expression, int line, ICollection<string>? warnings)
    {
        var parser = new Expression(expression);
        var value = parser.Parse();
        if (!parser.Failed)
            return value;
        warnings?.Add($"line {line}: preprocessor expression is not valid: {expression.Trim()}");
        return false;
    }

    private static bool VersionCheck(bool qt, int major, int minor, List<string> features)
    {
        if (qt)
        {
            // hasQtVersion has no feature list. A list keeps the exact language level from matching.
            return QtVersion.Major > major || (QtVersion.Major == major && QtVersion.Minor > minor)
                || (QtVersion == (major, minor) && features.Count == 0);
        }
        return ProductVersion.Current.Satisfies(major, minor, features);
    }

    /// <summary>A boolean expression. A failure is recorded and the value is then false.</summary>
    private sealed class Expression(string text)
    {
        private int _i;

        public bool Failed { get; private set; }

        public bool Parse()
        {
            var value = ParseOr();
            Skip();
            if (_i != text.Length)
                Failed = true;
            return value;
        }

        private bool ParseOr()
        {
            var value = ParseAnd();
            while (Match("||"))
            {
                var right = ParseAnd();
                value = value || right;
            }
            return value;
        }

        private bool ParseAnd()
        {
            var value = ParseUnary();
            while (Match("&&"))
            {
                var right = ParseUnary();
                value = value && right;
            }
            return value;
        }

        private bool ParseUnary()
        {
            if (Match("!"))
                return !ParseUnary();
            return ParsePrimary();
        }

        private bool ParsePrimary()
        {
            if (Match("true"))
                return true;
            if (Match("false"))
                return false;
            if (Match("("))
            {
                var value = ParseOr();
                if (!Match(")"))
                    Failed = true;
                return value;
            }
            if (Match("hasVersion"))
                return Call(qt: false);
            if (Match("hasQtVersion"))
                return Call(qt: true);
            Failed = true;
            return false;
        }

        private bool Call(bool qt)
        {
            if (!Match("(") || !TryNumber(out var major) || !Match(",") || !TryNumber(out var minor))
            {
                Failed = true;
                return false;
            }
            var features = new List<string>();
            if (Match(","))
            {
                if (!Match("["))
                {
                    Failed = true;
                    return false;
                }
                if (!Match("]"))
                {
                    do
                    {
                        if (!TryString(out var name))
                        {
                            Failed = true;
                            return false;
                        }
                        features.Add(name);
                    }
                    while (Match(","));
                    if (!Match("]"))
                    {
                        Failed = true;
                        return false;
                    }
                }
            }
            if (!Match(")"))
            {
                Failed = true;
                return false;
            }
            return Failed ? false : VersionCheck(qt, major, minor, features);
        }

        private bool TryNumber(out int value)
        {
            Skip();
            var start = _i;
            while (_i < text.Length && text[_i] is >= '0' and <= '9')
                _i++;
            if (start == _i || !int.TryParse(text.AsSpan(start, _i - start), NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                value = 0;
                return false;
            }
            return true;
        }

        private bool TryString(out string value)
        {
            Skip();
            if (_i >= text.Length || text[_i] != '"')
            {
                value = "";
                return false;
            }
            var start = ++_i;
            while (_i < text.Length && text[_i] != '"')
                _i++;
            if (_i >= text.Length)
            {
                value = "";
                return false;
            }
            value = text[start.._i];
            _i++;
            return true;
        }

        private bool Match(string token)
        {
            Skip();
            if (!text.AsSpan(_i).StartsWith(token))
                return false;
            var end = _i + token.Length;
            if (token[0] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')
                && end < text.Length
                && text[end] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                return false;
            _i = end;
            return true;
        }

        private void Skip()
        {
            while (_i < text.Length && char.IsWhiteSpace(text[_i]))
                _i++;
        }
    }
}
