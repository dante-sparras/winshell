using System.Text.RegularExpressions;

namespace Winshell.Runtime.Shell;

/// <summary>
/// Takes secrets out of text that goes into a crash report or the instance log: authorization headers, bearer
/// tokens, credentials assigned to a key or given to a flag that names one, the user and password and the query of
/// a URL, and long opaque tokens. It recognizes shapes, not meaning, so a report says that it was cleaned this way
/// rather than that it is safe.
/// </summary>
public static partial class CrashRedaction
{
    public const string Mask = "[redacted]";

    public static string Redact(string text)
    {
        text = UrlUserInfo().Replace(text, m => m.Groups["scheme"].Value + Mask + "@");
        text = UrlQuery().Replace(text, m => m.Groups["url"].Value + "?" + Mask);
        text = AuthorizationHeader().Replace(text, m => m.Groups["name"].Value + Mask);
        text = SchemeToken().Replace(text, m => m.Groups["scheme"].Value + " " + Mask);
        text = PassphraseLine().Replace(text, m => m.Groups["name"].Value + Mask);
        text = Assignment().Replace(text, m => m.Groups["key"].Value + m.Groups["sep"].Value + Mask);
        text = KnownTokenShape().Replace(text, Mask);
        text = Jwt().Replace(text, Mask);
        text = LongOpaque().Replace(text, m => IsRandom(m.Value) ? Mask : m.Value);
        return text;
    }

    // scheme://user:password@host
    [GeneratedRegex(@"(?<scheme>\b[a-z][a-z0-9+.\-]*://)[^/\s:@""'<>]+(?::[^/\s@""'<>]*)?@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlUserInfo();

    // Every query of a URL: its values are where links carry keys and signatures.
    [GeneratedRegex(@"(?<url>\b[a-z][a-z0-9+.\-]*://[^\s?#""'<>]*)\?[^\s#""'<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlQuery();

    // Authorization: Bearer abc / Proxy-Authorization: Basic abc / Cookie: a=b; c=d, to the end of the line.
    [GeneratedRegex(@"(?<name>\b(?:(?:proxy-)?authorization|cookie|set-cookie)\s*[:=]\s*)[^\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeader();

    // password = correct horse battery: an unquoted passphrase can have spaces, so it runs to the end of the line.
    [GeneratedRegex(@"(?<name>\b[A-Za-z0-9_\-]{0,64}(?:passw(?:or)?d|passphrase|pwd)\s*[:=]\s*)(?![""'])[^\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex PassphraseLine();

    [GeneratedRegex(@"(?<scheme>\b(?:bearer|basic|digest|token))\s+[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeToken();

    // password=..., "apiKey": "...", client_secret: ..., Cookie: ..., basic_auth=... The key may have a prefix or
    // suffix ("auth" only a prefix, so "Author:" is not a key), but no dot, so a stack frame's "Signals.cs:line 78"
    // or "TokenReader.cs:line 9" is not a key. A flag, --password hunter2 or /token abc, takes the next word as its
    // value, unless an earlier pattern already masked it; a bare word does not, so "the token expired" is left alone.
    // Bound the key's prefix and suffix: trying an unbounded prefix at each character makes long log lines quadratic.
    [GeneratedRegex(@"(?<key>(?<flag>(?<![\w\-/])(?:--?|/))?[A-Za-z0-9_\-]{0,64}(?:passw(?:or)?d|passphrase|pwd|secret|token|api[_\-.]?key|access[_\-.]?key|private[_\-.]?key|credential|cookie|session[_\-]?id|signature|auth(?=[""']?\s*[:=]|\s))[A-Za-z0-9_\-]{0,64}[""']?)(?<sep>\s*[:=]\s*|(?(flag)\s+|(?!)))(?!\[redacted\])(?:""(?:[^""\\\r\n]|\\.)*""|'(?:[^'\\\r\n]|\\.)*'|[^\s,;&""'}\])]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Assignment();

    // Keys whose prefix gives them away: GitHub, Slack, OpenAI-style and AWS access keys.
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|xox[abprs]-[A-Za-z0-9\-]{10,}|sk-[A-Za-z0-9\-_]{16,}|AKIA[A-Z0-9]{16})\b")]
    private static partial Regex KnownTokenShape();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*")]
    private static partial Regex Jwt();

    /// <summary>
    /// Whether a long run looks like a random token: digits and letters in both cases, and few dashes, unlike a
    /// word, a lowercase hash, or a dashed name such as a crash report's.
    /// </summary>
    private static bool IsRandom(string run) =>
        run.Any(char.IsAsciiDigit) && run.Any(char.IsAsciiLetterUpper) && run.Any(char.IsAsciiLetterLower) && run.Count(c => c == '-') <= 2;

    // 40 or more letters, digits, dashes and underscores in a row.
    [GeneratedRegex(@"[A-Za-z0-9_\-]{40,}")]
    private static partial Regex LongOpaque();
}
