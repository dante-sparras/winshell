using System.ComponentModel;
using System.Diagnostics;
using Winshell.Tray;
using Winshell.Winland;

namespace Winshell.DefaultShell.Taskbar;

/// <summary>
/// Winland's menu, which the shell draws for Winland's notification-area icon in place of the Win32 one Winland draws.
/// It has Winland's rows and access keys: Reload, Edit config, Open log while the log exists, and Quit. Reload and Quit
/// go through Winland's pipe. <paramref name="exists"/> tells whether a file is there, and <paramref name="open"/>
/// opens one; <see cref="Open"/> opens it as Winland does.
/// </summary>
public sealed class WinlandMenu(WinlandService winland, Func<string, bool> exists, Action<string> open)
{
    /// <summary>What Narrator calls the menu.</summary>
    public const string Name = "Winland";

    private readonly WinlandService _winland = winland;
    private readonly Func<string, bool> _exists = exists;
    private readonly Action<string> _open = open;

    /// <summary>
    /// Whether the icon is Winland's: the program that added it answers on Winland's pipe. While no Winland answers, no
    /// icon is, and a click reaches the app as on any other icon.
    /// </summary>
    public bool Owns(SystemTrayItem item) =>
        _winland.Executable.Length > 0 && string.Equals(item.Executable, _winland.Executable, StringComparison.OrdinalIgnoreCase);

    /// <summary>The rows as they stand now. Open log is there only while the log exists, as in Winland's menu.</summary>
    public IReadOnlyList<TaskMenuRow> Rows()
    {
        List<TaskMenuRow> rows = [Row("reload", "Reload", "R"), Row("editConfig", "Edit config", "E")];
        if (_exists(_winland.LogPath))
            rows.Add(Row("openLog", "Open log", "L"));
        rows.Add(Row("quit", "Quit", "Q"));
        return rows;
    }

    public void Choose(string id)
    {
        switch (id)
        {
            case "reload":
                _winland.Reload();
                break;
            case "editConfig":
                _open(_winland.ConfigPath);
                break;
            case "openLog":
                _open(_winland.LogPath);
                break;
            case "quit":
                _winland.Dispatch("exit");
                break;
        }
    }

    private static TaskMenuRow Row(string id, string label, string accessKey) => new(id, label, "", "", true, accessKey);

    /// <summary>
    /// Opens a file off the loop with the program Windows opens its type with, or in Notepad when the type has none,
    /// as Winland's own menu does.
    /// </summary>
    public static void Open(string path, Log log) => _ = Task.Run(() =>
    {
        try
        {
            Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
        }
        catch (Win32Exception)
        {
            try
            {
                var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
                Start(new ProcessStartInfo(notepad) { UseShellExecute = false, ArgumentList = { path } });
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                log.Warn("winland", $"Could not open {path}: {e.Message}");
            }
        }
        catch (InvalidOperationException e)
        {
            log.Warn("winland", $"Could not open {path}: {e.Message}");
        }
    });

    private static void Start(ProcessStartInfo start) => Process.Start(start)?.Dispose();
}
