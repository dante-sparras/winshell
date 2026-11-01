using System.Globalization;
using System.Text;
using System.Xml;

namespace Winshell.Network;

/// <summary>
/// Reads a WLAN profile and edits one element of the XML Windows returned.
/// The document is untrusted: DTD processing is off, nothing is resolved externally, and a document
/// past the size or depth cap is refused. The key text is never copied into a field or an error.
/// </summary>
internal static class WifiProfileXml
{
    internal const int MaxCharacters = 256 * 1024;
    internal const int MaxDepth = 32;
    internal const string TooLarge = "The Wi-Fi profile is too large to read.";
    internal const string Unreadable = "The Wi-Fi profile could not be read.";
    internal const string Rejected = "The Wi-Fi profile could not be changed.";

    internal readonly record struct Fields(
        string Name,
        string Ssid,
        string ConnectionMode,
        WifiSecurityType Security,
        bool HasPassword,
        bool Hidden)
    {
        public bool AutoConnect => ConnectionMode.Equals("auto", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Fields Empty = new("", "", "", WifiSecurityType.Unknown, false, false);

    public static bool TryParse(string? xml, out Fields fields, out string error)
    {
        fields = Empty;
        if (string.IsNullOrEmpty(xml))
        {
            error = Unreadable;
            return false;
        }
        if (xml.Length > MaxCharacters)
        {
            error = TooLarge;
            return false;
        }
        XmlDocument document;
        try
        {
            if (!WithinLimits(xml))
            {
                error = Unreadable;
                return false;
            }
            document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), ReaderSettings());
            document.Load(reader);
        }
        catch (XmlException)
        {
            error = Unreadable;
            return false;
        }
        if (document.DocumentElement is not { LocalName: "WLANProfile" } root)
        {
            error = Unreadable;
            return false;
        }
        fields = ReadFields(root);
        error = "";
        return true;
    }

    /// <summary>
    /// Replaces the profile's own <c>connectionMode</c> text and leaves every other byte as Windows sent it.
    /// <paramref name="changed"/> is false when the profile already has that mode, so the caller does not write.
    /// </summary>
    public static bool TrySetAutoConnect(string xml, bool auto, out string updated, out bool changed, out string error)
    {
        updated = xml;
        changed = false;
        if (!TryParse(xml, out var fields, out error))
            return false;
        var desired = auto ? "auto" : "manual";
        if (fields.ConnectionMode.Equals(desired, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!TryReplaceConnectionMode(xml, desired, out updated))
        {
            updated = xml;
            error = Rejected;
            return false;
        }
        if (!TryParse(updated, out var after, out error) || after.AutoConnect != auto || !SameExceptMode(fields, after))
        {
            updated = xml;
            error = Rejected;
            return false;
        }
        changed = true;
        error = "";
        return true;
    }

    /// <summary>Removes every <c>keyMaterial</c> element. Other elements, including ones this code does not know, stay.</summary>
    public static bool TryClearPassword(string xml, out string updated, out bool changed, out string error)
    {
        updated = xml;
        changed = false;
        if (!TryParse(xml, out var fields, out error))
            return false;
        if (!fields.HasPassword)
            return true;
        if (!TryRemoveKeyElements(xml, out updated))
        {
            updated = xml;
            error = Rejected;
            return false;
        }
        if (!TryParse(updated, out var after, out error) || after.HasPassword || !SameExceptPassword(fields, after))
        {
            updated = xml;
            error = Rejected;
            return false;
        }
        changed = true;
        error = "";
        return true;
    }

    private static bool SameExceptMode(Fields before, Fields after) =>
        before.Name == after.Name && before.Ssid == after.Ssid && before.Security == after.Security
        && before.HasPassword == after.HasPassword && before.Hidden == after.Hidden;

    private static bool SameExceptPassword(Fields before, Fields after) =>
        before.Name == after.Name && before.Ssid == after.Ssid && before.ConnectionMode == after.ConnectionMode
        && before.Security == after.Security && before.Hidden == after.Hidden;

    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxCharacters,
        MaxCharactersFromEntities = 1024,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = true,
    };

    /// <summary>Rejects a document that is too deep before a DOM is built. A DTD fails the reader and is not resolved.</summary>
    private static bool WithinLimits(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), ReaderSettings());
        while (reader.Read())
        {
            if (reader.Depth > MaxDepth)
                return false;
        }
        return true;
    }

    private static Fields ReadFields(XmlElement root)
    {
        string? mode = null;
        string? xmlName = null;
        string? ssidName = null;
        string? ssidHex = null;
        string? authentication = null;
        string? encryption = null;
        bool? hidden = null;
        var hasPassword = false;
        var pending = new Queue<(XmlElement Element, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Dequeue();
            if (element.LocalName == "keyMaterial")
            {
                hasPassword = true;
                continue;
            }
            if (depth == 1 && element.LocalName == "name" && xmlName is null)
                xmlName = ElementText(element);
            else if (depth == 1 && element.LocalName == "connectionMode" && mode is null)
                mode = ElementText(element).Trim();
            else if (element.LocalName == "nonBroadcast" && hidden is null)
                hidden = ElementText(element).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (element.LocalName == "authentication" && authentication is null)
                authentication = ElementText(element).Trim();
            else if (element.LocalName == "encryption" && encryption is null)
                encryption = ElementText(element).Trim();
            else if (element.LocalName == "SSID" && ssidName is null && ssidHex is null)
            {
                foreach (var child in ElementChildren(element))
                {
                    if (child.LocalName == "name")
                        ssidName ??= ElementText(child);
                    else if (child.LocalName == "hex")
                        ssidHex ??= ElementText(child).Trim();
                }
            }
            if (depth >= MaxDepth)
                continue;
            foreach (var child in ElementChildren(element))
                pending.Enqueue((child, depth + 1));
        }
        var ssid = !string.IsNullOrEmpty(ssidName) ? ssidName : DecodeHexSsid(ssidHex);
        return new Fields(xmlName ?? "", ssid, mode ?? "", MapSecurity(authentication, encryption), hasPassword, hidden == true);
    }

    /// <summary>The element's own text. Descendant text, including a nested key, is not read.</summary>
    private static string ElementText(XmlElement element)
    {
        var builder = new StringBuilder();
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlText or XmlCDataSection)
                builder.Append(child.Value);
        }
        return builder.ToString();
    }

    private static List<XmlElement> ElementChildren(XmlElement element)
    {
        var children = new List<XmlElement>();
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement elementChild)
                children.Add(elementChild);
        }
        return children;
    }

    private static string DecodeHexSsid(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || (hex.Length & 1) != 0)
            return "";
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return "";
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static WifiSecurityType MapSecurity(string? authentication, string? encryption)
    {
        var auth = authentication ?? "";
        var cipher = encryption ?? "";
        if (auth.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            return cipher.Equals("WEP", StringComparison.OrdinalIgnoreCase) ? WifiSecurityType.StaticWep : WifiSecurityType.Open;
        }
        if (auth.Equals("shared", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.StaticWep;
        if (auth.Equals("WPA", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.WpaEap;
        if (auth.Equals("WPAPSK", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.WpaPsk;
        if (auth.Equals("WPA2", StringComparison.OrdinalIgnoreCase) || auth.Equals("WPA3ENT", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.Wpa2Eap;
        if (auth.Equals("WPA2PSK", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.Wpa2Psk;
        if (auth.Equals("WPA3ENT192", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.Wpa3SuiteB192;
        if (auth.Equals("WPA3SAE", StringComparison.OrdinalIgnoreCase) || auth.Equals("WPA3", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.Sae;
        if (auth.Equals("OWE", StringComparison.OrdinalIgnoreCase))
            return WifiSecurityType.Owe;
        return WifiSecurityType.Unknown;
    }

    private static bool TryReplaceConnectionMode(string xml, string desired, out string updated)
    {
        updated = xml;
        if (desired is not ("auto" or "manual"))
            return false;
        if (!TryFindDirectChild(xml, "connectionMode", out var contentStart, out var contentEnd))
            return false;
        if (xml.AsSpan(contentStart, contentEnd - contentStart).Contains('<'))
            return false;
        updated = string.Concat(xml.AsSpan(0, contentStart), desired, xml.AsSpan(contentEnd));
        return true;
    }

    private static bool TryRemoveKeyElements(string xml, out string updated)
    {
        updated = xml;
        if (!TryCollectElements(xml, "keyMaterial", anyDepth: true, out var spans))
            return false;
        if (spans.Count == 0)
            return false;
        var builder = new StringBuilder(xml.Length);
        var cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start < cursor || span.End < span.Start || span.End > xml.Length)
                return false;
            builder.Append(xml, cursor, span.Start - cursor);
            cursor = span.End;
        }
        builder.Append(xml, cursor, xml.Length - cursor);
        updated = builder.ToString();
        return true;
    }

    private static bool TryFindDirectChild(string xml, string localName, out int contentStart, out int contentEnd)
    {
        contentStart = contentEnd = 0;
        if (!TryCollectElements(xml, localName, anyDepth: false, out var spans))
            return false;
        // A self-closing tag has no text to replace. An empty pair, <c></c>, does.
        if (spans.Count != 1 || spans[0].ContentStart >= spans[0].End)
            return false;
        contentStart = spans[0].ContentStart;
        contentEnd = spans[0].ContentEnd;
        return contentEnd >= contentStart;
    }

    private readonly record struct ElementSpan(int Start, int End, int ContentStart, int ContentEnd);

    /// <summary>
    /// Locates elements by walking the text. Comments, processing instructions and CDATA are not elements.
    /// A document the walker cannot follow is refused, so a write does not guess.
    /// </summary>
    private static bool TryCollectElements(string xml, string localName, bool anyDepth, out List<ElementSpan> spans)
    {
        spans = [];
        var i = 0;
        var depth = 0;
        while (i < xml.Length)
        {
            if (xml[i] != '<')
            {
                i++;
                continue;
            }
            var tagAt = i;
            if (!TryReadTag(xml, ref i, out var kind, out var qualified, out var selfClosing))
                return false;
            if (kind == TagKind.Ignored)
                continue;
            if (kind == TagKind.End)
            {
                depth--;
                if (depth < 0)
                    return false;
                continue;
            }
            var local = LocalName(qualified);
            var match = local == localName && (anyDepth || depth == 1);
            if (selfClosing)
            {
                if (match)
                    spans.Add(new ElementSpan(tagAt, i, i, i));
                continue;
            }
            if (match)
            {
                if (!TrySkipElement(xml, i, qualified, out var endTag, out var after))
                    return false;
                spans.Add(new ElementSpan(tagAt, after, i, endTag));
                i = after;
                continue;
            }
            depth++;
        }
        return depth == 0;
    }

    private enum TagKind { Start, End, Ignored }

    private static bool TryReadTag(string xml, ref int i, out TagKind kind, out string qualified, out bool selfClosing)
    {
        kind = TagKind.Ignored;
        qualified = "";
        selfClosing = false;
        if (i >= xml.Length || xml[i] != '<')
            return false;
        i++;
        if (i >= xml.Length)
            return false;
        if (xml[i] == '?')
        {
            i++;
            return SkipUntil(xml, ref i, "?>");
        }
        if (xml[i] == '!')
        {
            if (StartsWith(xml, i, "!--"))
            {
                i += 3;
                return SkipUntil(xml, ref i, "-->");
            }
            if (StartsWith(xml, i, "![CDATA["))
            {
                i += 8;
                return SkipUntil(xml, ref i, "]]>");
            }
            return false;
        }
        var end = xml[i] == '/';
        if (end)
            i++;
        var nameStart = i;
        if (i >= xml.Length || !IsNameStart(xml[i]))
            return false;
        i++;
        while (i < xml.Length && IsNameChar(xml[i]))
            i++;
        qualified = xml[nameStart..i];
        if (end)
        {
            while (i < xml.Length && char.IsWhiteSpace(xml[i]))
                i++;
            if (i >= xml.Length || xml[i] != '>')
                return false;
            i++;
            kind = TagKind.End;
            return true;
        }
        if (!SkipAttributes(xml, ref i, out selfClosing))
            return false;
        kind = TagKind.Start;
        return true;
    }

    private static bool TrySkipElement(string xml, int contentStart, string qualified, out int endTagStart, out int after)
    {
        endTagStart = after = 0;
        var depth = 1;
        var i = contentStart;
        while (i < xml.Length)
        {
            if (xml[i] != '<')
            {
                i++;
                continue;
            }
            var tagAt = i;
            if (!TryReadTag(xml, ref i, out var kind, out var name, out var selfClosing))
                return false;
            if (kind == TagKind.Ignored)
                continue;
            if (kind == TagKind.End)
            {
                depth--;
                if (depth == 0)
                {
                    if (name != qualified)
                        return false;
                    endTagStart = tagAt;
                    after = i;
                    return true;
                }
                continue;
            }
            if (!selfClosing)
                depth++;
        }
        return false;
    }

    private static bool SkipAttributes(string xml, ref int i, out bool selfClosing)
    {
        selfClosing = false;
        while (i < xml.Length)
        {
            while (i < xml.Length && char.IsWhiteSpace(xml[i]))
                i++;
            if (i >= xml.Length)
                return false;
            if (xml[i] == '>')
            {
                i++;
                return true;
            }
            if (xml[i] == '/' && i + 1 < xml.Length && xml[i + 1] == '>')
            {
                i += 2;
                selfClosing = true;
                return true;
            }
            if (!IsNameStart(xml[i]))
                return false;
            i++;
            while (i < xml.Length && IsNameChar(xml[i]))
                i++;
            while (i < xml.Length && char.IsWhiteSpace(xml[i]))
                i++;
            if (i >= xml.Length || xml[i] != '=')
                return false;
            i++;
            while (i < xml.Length && char.IsWhiteSpace(xml[i]))
                i++;
            if (i >= xml.Length || xml[i] is not ('"' or '\''))
                return false;
            var quote = xml[i++];
            while (i < xml.Length && xml[i] != quote)
                i++;
            if (i >= xml.Length)
                return false;
            i++;
        }
        return false;
    }

    private static bool SkipUntil(string xml, ref int i, string marker)
    {
        var found = xml.IndexOf(marker, i, StringComparison.Ordinal);
        if (found < 0)
            return false;
        i = found + marker.Length;
        return true;
    }

    private static bool StartsWith(string xml, int index, string text) =>
        index + text.Length <= xml.Length && xml.AsSpan(index, text.Length).SequenceEqual(text);

    private static string LocalName(string qualified)
    {
        var colon = qualified.LastIndexOf(':');
        return colon < 0 ? qualified : qualified[(colon + 1)..];
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or ':';

    private static bool IsNameChar(char c) => IsNameStart(c) || char.IsDigit(c) || c is '-' or '.';
}
