using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>
/// A failure before a shell exists. The message always goes to standard error. A dialog shows it only when
/// no console window is attached and nothing is reading that error, which is how the Start tile and the
/// sign-in entry run. Tests replace <see cref="Show"/>, so a test never opens a real dialog and a run
/// never waits on one.
/// </summary>
internal static partial class StartupError
{
    private const int StandardError = -12;
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;
    private const uint FileTypeRemote = 0x8000;

    /// <summary>Shows the message, and offers to open <paramref name="folder"/> when it is set. Tests replace this.</summary>
    internal static Action<string, string?> Show = Dialog;

    /// <summary>Reads this process's console and reports <paramref name="message"/>.</summary>
    internal static void Report(string message, string? folder) =>
        Report(message, folder, GetConsoleWindow(), StderrType());

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
        var handle = GetStdHandle(StandardError);
        return handle == 0 || handle == -1 ? 0 : GetFileType(handle);
    }

    private static void Dialog(string message, string? folder)
    {
        // CommCtrl.h packs this struct to 1. The default layout would misalign every pointer.
        var title = Marshal.StringToCoTaskMemUni("Winshell");
        var instruction = Marshal.StringToCoTaskMemUni("Winshell could not start");
        var content = Marshal.StringToCoTaskMemUni(message);
        var buttonText = folder is null ? 0 : Marshal.StringToCoTaskMemUni("Open config folder");
        var buttons = folder is null ? 0 : Marshal.AllocCoTaskMem(Marshal.SizeOf<DialogButton>());
        try
        {
            if (buttons != 0)
            {
                Marshal.WriteInt32(buttons, OpenConfigFolder);
                Marshal.WriteIntPtr(buttons, sizeof(int), buttonText);
            }
            var config = new DialogConfig
            {
                cbSize = (uint)Marshal.SizeOf<DialogConfig>(),
                dwFlags = AllowCancellation | SizeToContent,
                dwCommonButtons = OkButton,
                pszWindowTitle = title,
                mainIcon = ErrorIcon,
                pszMainInstruction = instruction,
                pszContent = content,
                cButtons = (uint)(buttons == 0 ? 0 : 1),
                pButtons = buttons,
                nDefaultButton = Ok,
            };
            var result = TaskDialogIndirect(ref config, out var button, out _, out _);
            if (result >= 0 && button == OpenConfigFolder && folder is not null)
                Open(folder);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The message is already on standard error. A dialog that cannot be shown must not crash the start.
        }
        finally
        {
            Marshal.FreeCoTaskMem(title);
            Marshal.FreeCoTaskMem(instruction);
            Marshal.FreeCoTaskMem(content);
            if (buttonText != 0)
                Marshal.FreeCoTaskMem(buttonText);
            if (buttons != 0)
                Marshal.FreeCoTaskMem(buttons);
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

    private const int Ok = 1;
    private const int OpenConfigFolder = 100;
    private const int AllowCancellation = 0x0008;
    private const int SizeToContent = 0x01000000;
    private const int OkButton = 0x0001;
    private static readonly nint ErrorIcon = 65534;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DialogButton
    {
        public int Id;
        public nint Text;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DialogConfig
    {
        public uint cbSize;
        public nint hwndParent;
        public nint hInstance;
        public int dwFlags;
        public int dwCommonButtons;
        public nint pszWindowTitle;
        public nint mainIcon;
        public nint pszMainInstruction;
        public nint pszContent;
        public uint cButtons;
        public nint pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public nint pRadioButtons;
        public int nDefaultRadioButton;
        public nint pszVerificationText;
        public nint pszExpandedInformation;
        public nint pszExpandedControlText;
        public nint pszCollapsedControlText;
        public nint footerIcon;
        public nint pszFooter;
        public nint pfCallback;
        public nint lpCallbackData;
        public uint cxWidth;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetFileType(nint handle);

    [LibraryImport("comctl32.dll")]
    private static partial int TaskDialogIndirect(ref DialogConfig config, out int button, out int radio, out int verification);
}
