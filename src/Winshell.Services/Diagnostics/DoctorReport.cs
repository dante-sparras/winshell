using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Winshell.Diagnostics;

/// <summary>The checks <see cref="Doctor"/> ran, as <c>winshell doctor</c> prints them: as text, or as JSON.</summary>
internal static class DoctorReport
{
    /// <summary>The checks as <c>winshell doctor</c> prints them: a line each, a fix under each problem, and a count.</summary>
    internal static string Format(IReadOnlyList<DoctorCheck> checks, string version, bool color)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Winshell {version}: this PC's setup");
        text.AppendLine();
        var width = checks.Max(c => c.Title.Length) + 2;
        foreach (var check in checks)
        {
            var label = check.Status switch
            {
                CheckStatus.Pass => "PASS",
                CheckStatus.Warn => "WARN",
                _ => "FAIL",
            };
            if (color)
                label = (check.Status switch { CheckStatus.Pass => "\e[32m", CheckStatus.Warn => "\e[33m", _ => "\e[31m" }) + label + "\e[0m";
            text.AppendLine(CultureInfo.InvariantCulture, $"  {label}  {check.Title.PadRight(width)}{check.Message}");
            if (check.Fix is not null)
                text.AppendLine(CultureInfo.InvariantCulture, $"        {new string(' ', width)}Fix: {check.Fix}");
        }
        text.AppendLine();
        text.AppendLine(Summary(checks));
        return text.ToString();
    }

    internal static string Summary(IReadOnlyList<DoctorCheck> checks)
    {
        var warns = checks.Count(c => c.Status == CheckStatus.Warn);
        var fails = checks.Count(c => c.Status == CheckStatus.Fail);
        return string.Create(CultureInfo.InvariantCulture,
            $"{checks.Count(c => c.Status == CheckStatus.Pass)} passed, {warns} {(warns == 1 ? "warning" : "warnings")}, {fails} failed.");
    }

    /// <summary>The checks as <c>winshell doctor --json</c> prints them.</summary>
    internal static string ToJson(IReadOnlyList<DoctorCheck> checks, string version)
    {
        using var stream = new MemoryStream();
        // Read by people too: paths and messages are written as they are, not escaped for HTML.
        using (var json = new Utf8JsonWriter(stream, JsonText.IndentedWriter))
        {
            json.WriteStartObject();
            json.WriteString("version", version);
            json.WriteStartArray("checks");
            foreach (var check in checks)
            {
                json.WriteStartObject();
                json.WriteString("id", check.Id);
                json.WriteString("title", check.Title);
                json.WriteString("status", Doctor.StatusName(check.Status));
                json.WriteString("message", check.Message);
                if (check.Fix is null)
                    json.WriteNull("fix");
                else
                    json.WriteString("fix", check.Fix);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("summary");
            json.WriteNumber("pass", checks.Count(c => c.Status == CheckStatus.Pass));
            json.WriteNumber("warn", checks.Count(c => c.Status == CheckStatus.Warn));
            json.WriteNumber("fail", checks.Count(c => c.Status == CheckStatus.Fail));
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
