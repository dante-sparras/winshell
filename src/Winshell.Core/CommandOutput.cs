using System.Text;
using System.Text.Json;

namespace Winshell.Core;

/// <summary>Reads the command line a script widget runs, and the text it prints.</summary>
public static class CommandOutput
{
    /// <summary>The most of a script widget's output that is read, in bytes. The widget shows one line, so a program that prints more is not heard out.</summary>
    public const int MaxBytes = 64 * 1024;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The text a script widget's program printed. Bytes that are valid UTF-8 are read as UTF-8. Any others are read in the
    /// console's code page, which is what a program that sets no encoding prints in. A byte order mark at the start is dropped.
    /// Output that fills <see cref="MaxBytes"/> may end inside a character, and that cut character is dropped.
    /// </summary>
    /// <param name="bytes">What the program printed, <see cref="MaxBytes"/> of it at most.</param>
    /// <param name="console">The console's code page.</param>
    /// <returns>The output as text.</returns>
    public static string Decode(ReadOnlySpan<byte> bytes, Encoding console)
    {
        ArgumentNullException.ThrowIfNull(console);
        var cut = bytes.Length >= MaxBytes;

        // A byte order mark is not a space, so it would otherwise stay in front of the text and hide a JSON object's brace.
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        try
        {
            // A decoder that is not flushed keeps a character whose last bytes are missing, where a flushed one refuses it.
            // UTF-8 never decodes to more characters than it has bytes.
            var chars = new char[bytes.Length];
            return new string(chars, 0, Strict.GetDecoder().GetChars(bytes, chars, flush: !cut));
        }
        catch (DecoderFallbackException)
        {
            return console.GetString(bytes);
        }
    }

    /// <summary>
    /// Splits a script widget's command line into the program and its arguments.
    /// A program in double quotes may hold spaces. Otherwise the program ends at the first space.
    /// </summary>
    /// <param name="exec">The slot's <c>exec</c> or <c>onClick</c>.</param>
    /// <returns>The program to start and the rest of the line.</returns>
    public static (string File, string Args) Split(string exec)
    {
        ArgumentNullException.ThrowIfNull(exec);
        exec = exec.Trim();
        if (exec.StartsWith('"'))
        {
            var end = exec.IndexOf('"', 1);
            if (end > 1)
            {
                return (exec[1..end], exec[(end + 1)..].Trim());
            }
        }

        var space = exec.IndexOf(' ');
        return space < 0 ? (exec, "") : (exec[..space], exec[(space + 1)..]);
    }

    /// <summary>
    /// Plain text uses the first line. A JSON object may set <c>text</c> and <c>tooltip</c>.
    /// JSON that does not parse is shown as plain text.
    /// </summary>
    public static (string Text, string Tooltip) Read(string? stdout)
    {
        var raw = (stdout ?? "").Trim();
        if (raw.Length == 0)
        {
            return ("", "");
        }

        if (raw[0] == '{')
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var text = document.RootElement.TryGetProperty("text", out var textValue) && textValue.ValueKind == JsonValueKind.String
                        ? textValue.GetString() ?? ""
                        : "";
                    var tooltip = document.RootElement.TryGetProperty("tooltip", out var tipValue) && tipValue.ValueKind == JsonValueKind.String
                        ? tipValue.GetString() ?? ""
                        : "";
                    return (text, tooltip);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // A string that holds half a surrogate pair parses and then throws InvalidOperationException when it is read.
            }
        }

        var line = raw.Split('\n', 2)[0].Trim();
        return (line, "");
    }
}
