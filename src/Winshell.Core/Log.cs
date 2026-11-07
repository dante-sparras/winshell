namespace Winshell;

/// <summary>How serious a log message is, from least to most.</summary>
public enum LogLevel
{
    /// <summary>Detail for tracking down a problem. Kept only when the shell runs with <c>--verbose</c>.</summary>
    Debug,

    /// <summary>Normal progress worth recording.</summary>
    Info,

    /// <summary>Something went wrong and the shell carried on.</summary>
    Warn,

    /// <summary>An operation failed.</summary>
    Error,

    /// <summary>A failure that ends the process.</summary>
    Fatal
}

/// <summary>Writes messages as <c> WARN category: message</c>.</summary>
public sealed class Log
{
    /// <summary>
    /// This process's log. In a running shell it writes to the instance's log, which <c>winshell log</c> shows.
    /// </summary>
    public static readonly Log Default = new();

    private readonly object _lock = new();

    internal Log()
    {
    }

    /// <summary>Where messages go. The default writes to the standard error stream.</summary>
    internal Action<LogLevel, string, string>? Sink { get; set; }

    internal LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>
    /// A file that reports to this log changed whether it reads: its path and why not, or empty once it reads again.
    /// Under <c>winshell dev</c> the shell shows it on screen.
    /// </summary>
    internal event Action<string, string>? FileErrorChanged;

    internal void ReportFileError(string path, string error)
    {
        FileErrorChanged?.Invoke(path, error);
    }

    /// <summary>
    /// Writes <paramref name="message"/> under <paramref name="category"/>, such as <c>tray</c>. A
    /// <see cref="LogLevel.Debug"/> message is dropped unless the shell runs with <c>--verbose</c>.
    /// </summary>
    /// <param name="level">How serious the message is.</param>
    /// <param name="category">A short word for the part of the shell the message is about. May be empty.</param>
    /// <param name="message">The text of the message.</param>
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
            _ => "FATAL"
        };
        var line = string.IsNullOrEmpty(category) ? $"{label}: {message}" : $"{label} {category}: {message}";
        lock (_lock)
        {
            Console.Error.WriteLine(line);
        }
    }

    /// <summary>
    /// Writes a <see cref="LogLevel.Debug"/> message, kept only when the shell runs with <c>--verbose</c>.
    /// </summary>
    public void Debug(string category, string message)
    {
        Write(LogLevel.Debug, category, message);
    }

    /// <summary>Writes a <see cref="LogLevel.Info"/> message.</summary>
    public void Info(string category, string message)
    {
        Write(LogLevel.Info, category, message);
    }

    /// <summary>Writes a <see cref="LogLevel.Warn"/> message.</summary>
    public void Warn(string category, string message)
    {
        Write(LogLevel.Warn, category, message);
    }

    /// <summary>Writes a <see cref="LogLevel.Error"/> message.</summary>
    public void Error(string category, string message)
    {
        Write(LogLevel.Error, category, message);
    }
}
