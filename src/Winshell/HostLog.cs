using System.Globalization;

namespace Winshell;

/// <summary>Appends the shell's log to <c>winshell.log</c> in its config folder.</summary>
internal sealed class HostLog : ILog
{
    private readonly string _path;
    private readonly object _gate = new();

    public HostLog(string path)
    {
        _path = path;
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Write(LogSeverity severity, string area, string message, Exception? error = null)
    {
        var line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
            + " " + severity.ToString().ToLowerInvariant()
            + " " + area + " " + message;
        if (error is not null)
        {
            line += " " + error.GetType().Name + ": " + error.Message;
        }

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
