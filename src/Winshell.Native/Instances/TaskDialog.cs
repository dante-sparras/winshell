using Windows.Win32;
using Windows.Win32.UI.Controls;

namespace Winshell.Native;

/// <summary>A task dialog, which needs version 6 of the common controls in the program's manifest.</summary>
internal static unsafe class TaskDialog
{
    private const int Ok = 1;

    /// <summary>
    /// Shows an error with an OK button, and <paramref name="button"/> beside it when set. Returns the id of the button
    /// pressed, or null when the dialog could not be shown.
    /// </summary>
    internal static int? ShowError(string title, string instruction, string content, (int Id, string Text)? button)
    {
        fixed (char* titleText = title, instructionText = instruction, contentText = content, buttonText = button?.Text)
        {
            var custom = new TASKDIALOG_BUTTON { nButtonID = button?.Id ?? 0, pszButtonText = buttonText };
            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(TASKDIALOGCONFIG),
                dwFlags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION | TASKDIALOG_FLAGS.TDF_SIZE_TO_CONTENT,
                dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_OK_BUTTON,
                pszWindowTitle = titleText,
                pszMainInstruction = instructionText,
                pszContent = contentText,
                cButtons = button is null ? 0u : 1u,
                pButtons = button is null ? null : &custom,
                nDefaultButton = Ok,
            };
            config.pszMainIcon = PInvoke.TD_ERROR_ICON;
            int pressed;
            return PInvoke.TaskDialogIndirect(&config, &pressed, null, null).Succeeded ? pressed : null;
        }
    }
}
