using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Platform.Windows.Services.Hyprland;

/// <summary>
/// How Winland's pipe speaks. A request is one line. A reply is one line, or a <c>#&lt;length&gt;</c> line and then
/// that many bytes. An <c>events</c> request gets no reply and stays open, one <c>name&gt;&gt;data</c> line per event.
/// </summary>
internal static class WinlandProtocol
{
    public const int ReplyLimit = 1024 * 1024;

    /// <summary>
    /// The pipe names to try, in order. <c>WINLAND_PIPE</c> names it; otherwise it is <c>winland-&lt;session&gt;</c>,
    /// and older builds of Winland used plain <c>winland</c>.
    /// </summary>
    public static IReadOnlyList<string> PipeNames(string? overrideName, int sessionId) =>
        string.IsNullOrWhiteSpace(overrideName)
            ? ["winland-" + sessionId.ToString(CultureInfo.InvariantCulture), "winland"]
            : [overrideName.Trim()];

    /// <summary>An event line split into its name and its data.</summary>
    public static (string Name, string Data) SplitEvent(string line)
    {
        var split = line.IndexOf(">>", StringComparison.Ordinal);
        return split < 0 ? (line, "") : (line[..split], line[(split + 2)..]);
    }

    /// <summary>
    /// The data of an event split into <paramref name="count"/> arguments, as Quickshell does: the last one keeps any
    /// commas, and missing ones are empty.
    /// </summary>
    public static string[] ParseArgs(string data, int count)
    {
        var args = new List<string>(Math.Max(count, 1));
        var rest = data.AsSpan();
        for (var i = 0; i < count - 1; i++)
        {
            var comma = rest.IndexOf(',');
            if (comma < 0)
                break;
            args.Add(rest[..comma].ToString());
            rest = rest[(comma + 1)..];
        }
        if (!rest.IsEmpty)
            args.Add(rest.ToString());
        while (args.Count < count)
            args.Add("");
        return [.. args];
    }

    /// <summary>A window address from an event or a reply, with or without <c>0x</c>. Zero when it is not one.</summary>
    public static ulong ParseAddress(string text)
    {
        var s = text.AsSpan().Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        return ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>The id Winland gives a Workspace in events when the JSON leaves it out: special ones from -99, named ones from -1.</summary>
    public static Dictionary<string, int> UnnumberedIds(IEnumerable<string> names)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var distinct = names.Distinct(StringComparer.Ordinal).Where(n => !int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)).ToList();
        var named = distinct.Where(n => !IsSpecial(n)).Order(StringComparer.Ordinal).ToList();
        for (var i = 0; i < named.Count; i++)
            ids[named[i]] = -1 - i;
        var next = -98;
        foreach (var special in distinct.Where(IsSpecial).Order(StringComparer.Ordinal))
            ids[special] = special == DefaultSpecial ? -99 : next++;
        return ids;
    }

    public const string DefaultSpecial = "special:special";

    public static bool IsSpecial(string name) => name.StartsWith("special:", StringComparison.Ordinal);

    /// <summary>The id of a Workspace known only by its name.</summary>
    public static int IdFromName(string name) =>
        int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : name == DefaultSpecial ? -99 : -1;

    /// <summary>A JSON value as QML sees it: objects become maps, arrays lists.</summary>
    public static object? ToQml(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToQml(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToQml).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) && l is >= int.MinValue and <= int.MaxValue ? (object)(int)l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static string Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static int? Int(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static double Num(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public static bool Bool(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static JsonElement Obj(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) ? v : default;
}

/// <summary>Reads Winland's lines and framed replies from a stream, with a buffer of its own.</summary>
internal sealed class WinlandReader(Stream stream)
{
    private readonly byte[] _buffer = new byte[8192];
    private int _start;
    private int _end;

    /// <summary>The text up to the next newline, without a carriage return. Null when the stream ends before any byte.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancel = default)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (_start == _end && !await FillAsync(cancel).ConfigureAwait(false))
                return line.Length == 0 ? null : Decode(line);
            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            var stop = newline < 0 ? _end : newline;
            line.Write(_buffer, _start, stop - _start);
            if (line.Length > WinlandProtocol.ReplyLimit)
                throw new InvalidDataException("The line is too long.");
            _start = newline < 0 ? _end : newline + 1;
            if (newline >= 0)
                return Decode(line);
        }
    }

    /// <summary>One reply: a line, or the body a <c>#length</c> line frames. Empty when the stream ends first.</summary>
    public async Task<string> ReadReplyAsync(CancellationToken cancel = default)
    {
        if (await ReadLineAsync(cancel).ConfigureAwait(false) is not { } first)
            return "";
        if (first.Length <= 1 || first[0] != '#' || !int.TryParse(first.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            return first;
        if (length > WinlandProtocol.ReplyLimit)
            throw new InvalidDataException("The reply is too large.");
        var body = new byte[length];
        var got = 0;
        while (got < length)
        {
            if (_start == _end && !await FillAsync(cancel).ConfigureAwait(false))
                throw new EndOfStreamException("The reply was cut short.");
            var take = Math.Min(length - got, _end - _start);
            Array.Copy(_buffer, _start, body, got, take);
            _start += take;
            got += take;
        }
        return Encoding.UTF8.GetString(body);
    }

    private async Task<bool> FillAsync(CancellationToken cancel)
    {
        _start = 0;
        _end = await stream.ReadAsync(_buffer, cancel).ConfigureAwait(false);
        return _end > 0;
    }

    private static string Decode(MemoryStream line)
    {
        var bytes = line.GetBuffer().AsSpan(0, (int)line.Length);
        if (bytes.Length > 0 && bytes[^1] == '\r')
            bytes = bytes[..^1];
        return Encoding.UTF8.GetString(bytes);
    }
}
