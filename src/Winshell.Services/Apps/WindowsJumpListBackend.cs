using Winshell.Native;

namespace Winshell.Apps;

/// <summary>
/// Reads another app's recent and frequent documents. That is the list Windows will give. Pinned jump-list items
/// and custom tasks are not read: <c>ICustomDestinationList</c> opens a list so the caller can replace it, and
/// Windows does not give another app's pinned list to a reader. The shell's UI thread is an STA, which
/// <see cref="DocumentLists"/> requires. A failed read is <see cref="JumpListRead.Missing"/>, and the menu says the
/// list cannot be read.
/// </summary>
internal sealed class WindowsJumpListBackend : IJumpListBackend
{
    public JumpListRead Read(string appUserModelId)
    {
        var (recent, frequent) = DocumentLists.Read(appUserModelId, JumpListRead.PerCategory);
        return JumpListRead.From(recent, frequent);
    }
}
