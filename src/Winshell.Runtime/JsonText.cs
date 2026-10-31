using System.Text.Encodings.Web;
using System.Text.Json;

namespace Winshell.Runtime;

/// <summary>
/// JSON a person reads. Characters outside ASCII stay as themselves, which is how
/// <c>winshell doctor --json</c> writes a path.
/// </summary>
public static class JsonText
{
    public static JavaScriptEncoder Encoder { get; } = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    public static JsonWriterOptions IndentedWriter { get; } = new()
    {
        Indented = true,
        Encoder = Encoder,
    };
}
