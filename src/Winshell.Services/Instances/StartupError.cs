using System.Diagnostics;
using Winshell.Native;

namespace Winshell;

/// <summary>
/// A failure before a shell exists. The message always goes to standard error. A dialog shows it only when
/// no console window is attached and nothing is reading that error, which is how the Start tile and the
/// sign-in entry run. Tests replace <see cref="Show"/>, so a test never opens a real dialog and a run
/// never waits on one.
/// </summary>
internal static class StartupError
{
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;
    private const uint FileTypeRemote = 0x8000;

    /// <summary>
    /// Shows the message, the first argument, and offers to open the folder, the second, when it is set. Tests replace
    /// this.
    /// </summary>
    internal static Action<string, string?> Show = Dialog;

    /// <summary>Reads this process's console and reports <paramref name="message"/>.</summary>
    internal static void Report(string message, string? folder) =>
        Report(message, folder, ProcessConsole.Window(), StderrType());

    /// <summary>
    /// Writes <paramref name="message"/> to standard error, then asks <see cref="Show"/> when
    /// <paramref name="consoleWindow"/> is zero and <paramref name="stderrType"/> is not a pipe or a file.
    /// </summary>
    internal static void Report(string message, string? folder, nint consoleWindow, uint stderrType)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch (IOException)
        {
            // A windowless start can have no standard error. The dialog is the message then.
        }
        if (!NeedsDialog(consoleWindow, stderrType))
            return;
        Show(message, folder);
    }

    /// <summary>
    /// True when the error would otherwise be invisible. A console window is enough on its own.
    /// A pipe or a file means a parent is reading standard error, which is how the tests run.
    /// </summary>
    internal static bool NeedsDialog(nint consoleWindow, uint stderrType)
    {
        if (consoleWindow != 0)
            return false;
        var kind = stderrType & ~FileTypeRemote;
        return kind is not (FileTypeDisk or FileTypePipe);
    }

    /// <summary>
    /// The folder a dialog can open. <paramref name="path"/> is <c>--path</c>: that directory, or its parent
    /// when the path is a missing file. With no path, the config root, when that directory exists.
    /// </summary>
    internal static string? FolderFor(string? path, string configRoot)
    {
        if (path is not null)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
                return full;
            var parent = Path.GetDirectoryName(full);
            return parent is not null && Directory.Exists(parent) ? parent : null;
        }
        return Directory.Exists(configRoot) ? configRoot : null;
    }

    private static uint StderrType()
    {
        var handle = StandardHandles.Error;
        return handle == 0 || handle == -1 ? 0 : ProcessConsole.FileType(handle);
    }

    private static void Dialog(string message, string? folder)
    {
        try
        {
            var pressed = TaskDialog.ShowError("Winshell", "Winshell could not start", message,
                folder is null ? null : (OpenConfigFolder, "Open config folder"));
            if (pressed == OpenConfigFolder && folder is not null)
                Open(folder);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The message is already on standard error. A dialog that cannot be shown must not crash the start.
        }
    }

    private static void Open(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
    }

    private const int OpenConfigFolder = 100;
}
