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
public sealed class Log
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
    public void ScriptError(Exception e, string where)
    {
        var message = e switch
        {
            JavaScriptException js => $"{ErrorName(js)}: {js.Message}",
            QmlException q => q.Message,
            Jint.Runtime.RecursionDepthOverflowException => "RangeError: Maximum call stack size exceeded",
            _ => $"{e.GetType().Name}: {e.Message}",
        };
        Warn("scene", $"{where}: {message}");
    }

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
