using System.Globalization;
using System.Text.RegularExpressions;
using Jint;
using Jint.Runtime;

namespace Winshell.Runtime;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal,
}

/// <summary>Writes messages in Quickshell's format: <c> WARN scene: shell.qml[36:-1]: ReferenceError: ...</c>.</summary>
public sealed partial class Log
{
    public static readonly Log Default = new();

    private readonly object _lock = new();

    /// <summary>Where messages go. The default writes to the standard error stream.</summary>
    public Action<LogLevel, string, string>? Sink { get; set; }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public void Write(LogLevel level, string category, string message)
    {
        if (level < MinimumLevel)
            return;
        if (Sink is { } sink)
        {
            sink(level, category, message);
            return;
        }
        var label = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => " INFO",
            LogLevel.Warn => " WARN",
            LogLevel.Error => "ERROR",
            _ => "FATAL",
        };
        var line = string.IsNullOrEmpty(category) ? $"{label}: {message}" : $"{label} {category}: {message}";
        lock (_lock)
            Console.Error.WriteLine(line);
    }

    public void Debug(string category, string message) => Write(LogLevel.Debug, category, message);

    public void Info(string category, string message) => Write(LogLevel.Info, category, message);

    public void Warn(string category, string message) => Write(LogLevel.Warn, category, message);

    public void Error(string category, string message) => Write(LogLevel.Error, category, message);

    /// <summary>Reports an exception thrown by script code, as QML reports it.</summary>
    public void ScriptError(Exception e, string where) => Warn("scene", Describe(e, where));

    /// <summary>
    /// An exception as QML reports it: where it happened, then the error. A script error names the file and line of
    /// the statement that threw; <paramref name="where"/> is used when the error does not say.
    /// </summary>
    public static string Describe(Exception e, string where)
    {
        if (e is JavaScriptException js)
        {
            var message = TrailingLocation().Replace(js.Message, "");
            return $"{ScriptLocation(js) ?? where}: {ErrorName(js)}: {message}";
        }
        return e switch
        {
            Qml.QmlSyntaxException or QmlCompileException => e.Message,
            QmlException q => $"{where}: {q.Message}",
            RecursionDepthOverflowException => $"{where}: RangeError: Maximum call stack size exceeded",
            _ => $"{where}: {e.GetType().Name}: {e.Message}",
        };
    }

    /// <summary>
    /// The QML position of a script error. Script code is compiled with the position it starts at as its source,
    /// <c>file[line:column]</c>, so its lines count from there.
    /// </summary>
    private static string? ScriptLocation(JavaScriptException js)
    {
        var location = js.Location;
        if (string.IsNullOrEmpty(location.SourceFile) || location.Start.Line <= 0)
            return null;
        var line = location.Start.Line;
        if (QmlSource().Match(location.SourceFile) is { Success: true } m)
        {
            var startLine = int.Parse(m.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
            // On the first line the code follows the wrapper the runtime adds, so its column is the code's start.
            var column = line == 1 ? m.Groups[3].Value : (location.Start.Column + 1).ToString(CultureInfo.InvariantCulture);
            return $"{m.Groups[1].Value}[{startLine + line - 1}:{column}]";
        }
        return $"{location.SourceFile}[{line}:{location.Start.Column + 1}]";
    }

    [GeneratedRegex(@"^(.*)\[(\d+):(\d+)\]$")]
    private static partial Regex QmlSource();

    [GeneratedRegex(@" \([^()]*:\d+:\d+\)$")]
    private static partial Regex TrailingLocation();

    private static string ErrorName(JavaScriptException js)
    {
        try
        {
            var name = js.Error.IsObject() ? js.Error.AsObject().Get("name") : null;
            return name is not null && name.IsString() ? name.AsString() : "Error";
        }
        catch (JavaScriptException)
        {
            return "Error";
        }
    }
}
