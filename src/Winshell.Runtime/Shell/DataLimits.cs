using System.Globalization;
using System.Text;

namespace Winshell.Runtime.Shell;

/// <summary>
/// How much of a data file Winshell will read. <c>preferences.toml</c> is a dozen keys and a few kilobytes.
/// The largest QML file in the default shell is about 22 KB, and the shell's QML together is about 200 KB.
/// These limits sit far above either and still stop a read from taking a multi-megabyte file or a parse from
/// building an unbounded tree.
/// </summary>
internal static class DataLimits
{
    /// <summary>1 MiB. A file this large is not a preferences file or one QML document.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>
    /// Values, tables and array elements. A long pin list is a few thousand of these. A denser document is refused
    /// before Tomlyn builds it.
    /// </summary>
    public const int MaxNodes = 100_000;

    /// <summary>
    /// Nested tables and arrays, including a dotted key. A preferences value is one or two levels
    /// (<c>hiddenNotificationApps = [{ id, name }]</c>). Tomlyn's own container limit is 64 and does not count
    /// dotted keys, so this is checked first.
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>Null when <paramref name="bytes"/> is within <paramref name="maxBytes"/>.</summary>
    public static string? FileBytes(long bytes, int maxBytes) =>
        bytes > maxBytes
            ? string.Create(CultureInfo.InvariantCulture, $"file is {bytes} bytes, above the limit of {maxBytes}")
            : null;

    public static bool IsFileLimit(string message) =>
        message.Contains("above the limit of ", StringComparison.Ordinal);

    /// <summary>
    /// Null when <paramref name="text"/> is within the limits. The scan does not allocate a tree and does not recurse,
    /// so a file that fails it is never handed to Tomlyn.
    /// </summary>
    public static string? Toml(string text, int maxBytes, int maxNodes, int maxDepth)
    {
        if (text.Length > maxBytes || Encoding.UTF8.GetByteCount(text) > maxBytes)
            return FileBytes(Encoding.UTF8.GetByteCount(text), maxBytes);
        return Structure(text, maxNodes, maxDepth);
    }

    /// <summary>
    /// Depth is the number of tables and arrays a value sits inside. A dotted key adds one level per dot. A table
    /// header counts its segments, and an array of tables counts one more. Brackets and quotes inside strings and
    /// comments do not count.
    /// </summary>
    private static string? Structure(string text, int maxNodes, int maxDepth)
    {
        var nodes = 0;
        var scope = 0;
        var keyExtra = 0;
        var pending = 0;
        var top = 0;
        var inKey = true;
        var lineStart = true;
        var inHeader = false;
        var headerArray = false;
        var mode = 0;
        var escape = false;
        Span<int> scopes = stackalloc int[256];
        Span<byte> kinds = stackalloc byte[256];

        string TooDeep() => string.Create(CultureInfo.InvariantCulture, $"nested deeper than {maxDepth}");
        string TooMany() => string.Create(CultureInfo.InvariantCulture, $"has more than {maxNodes} values");

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (mode == 1)
            {
                if (c is not '\n' and not '\r')
                    continue;
                mode = 0;
            }
            else if (mode >= 2)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }
                if ((mode is 2 or 4) && c == '\\')
                {
                    escape = true;
                    continue;
                }
                if (mode == 2 && c == '"')
                {
                    mode = 0;
                    continue;
                }
                if (mode == 3 && c == '\'')
                {
                    mode = 0;
                    continue;
                }
                if ((mode == 4 && c == '"' || mode == 5 && c == '\'')
                    && i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c)
                {
                    mode = 0;
                    i += 2;
                }
                continue;
            }

            if (c is ' ' or '\t')
                continue;
            if (c is '\n' or '\r')
            {
                lineStart = true;
                keyExtra = 0;
                pending = 0;
                inHeader = false;
                headerArray = false;
                inKey = top == 0 || kinds[top - 1] == 2;
                continue;
            }
            if (c == '#')
            {
                mode = 1;
                lineStart = false;
                continue;
            }
            if (c is '"' or '\'')
            {
                var triple = i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c;
                mode = c == '"' ? triple ? 4 : 2 : triple ? 5 : 3;
                if (triple)
                    i += 2;
                lineStart = false;
                continue;
            }
            if (c == '.')
            {
                if (inKey || inHeader)
                {
                    keyExtra++;
                    var at = inHeader ? keyExtra : scope + keyExtra;
                    if (at > maxDepth)
                        return TooDeep();
                    if (++nodes > maxNodes)
                        return TooMany();
                }
                lineStart = false;
                continue;
            }
            if (c == '=')
            {
                if (scope + keyExtra > maxDepth)
                    return TooDeep();
                if (++nodes > maxNodes)
                    return TooMany();
                pending = keyExtra;
                keyExtra = 0;
                inKey = false;
                inHeader = false;
                lineStart = false;
                continue;
            }
            if (c == '{' || (c == '[' && !(lineStart && top == 0)))
            {
                var inside = scope + pending + 1;
                if (inside > maxDepth || top == scopes.Length)
                    return TooDeep();
                if (++nodes > maxNodes)
                    return TooMany();
                scopes[top] = scope;
                kinds[top] = c == '[' ? (byte)1 : (byte)2;
                top++;
                scope = inside;
                pending = 0;
                keyExtra = 0;
                inKey = c == '{';
                lineStart = false;
                continue;
            }
            if (c == '[')
            {
                inHeader = true;
                headerArray = i + 1 < text.Length && text[i + 1] == '[';
                if (headerArray)
                {
                    i++;
                    if (++nodes > maxNodes)
                        return TooMany();
                }
                if (++nodes > maxNodes)
                    return TooMany();
                keyExtra = 0;
                lineStart = false;
                continue;
            }
            if (c == ']')
            {
                if (inHeader)
                {
                    var tableAt = keyExtra + 1 + (headerArray ? 1 : 0);
                    if (tableAt > maxDepth)
                        return TooDeep();
                    scope = tableAt;
                    inHeader = false;
                    headerArray = false;
                    keyExtra = 0;
                    inKey = false;
                }
                else if (top > 0)
                {
                    top--;
                    scope = scopes[top];
                    inKey = false;
                    keyExtra = 0;
                }
                pending = 0;
                lineStart = false;
                continue;
            }
            if (c == '}')
            {
                if (top > 0)
                {
                    top--;
                    scope = scopes[top];
                }
                pending = 0;
                keyExtra = 0;
                inKey = false;
                lineStart = false;
                continue;
            }
            if (c == ',')
            {
                if (++nodes > maxNodes)
                    return TooMany();
                pending = 0;
                keyExtra = 0;
                inKey = top > 0 && kinds[top - 1] == 2;
                lineStart = false;
                continue;
            }
            lineStart = false;
        }
        return null;
    }
}
