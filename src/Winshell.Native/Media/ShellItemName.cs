using System.Runtime.InteropServices;

namespace Winshell.Native;

internal static partial class ShellItemName
{
    /// <summary>
    /// The name the shell shows for the item at <paramref name="parsingName"/>, such as
    /// <c>shell:AppsFolder\{AppUserModelID}</c>, or null when it has no such item.
    /// </summary>
    internal static string? Find(string parsingName)
    {
        if (SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItem).GUID, out var pointer) < 0 || pointer == 0)
            return null;
        IShellItem? item = null;
        try
        {
            item = (IShellItem)Marshal.GetObjectForIUnknown(pointer);
            return item.GetDisplayName(0, out var name) == 0 ? name : null;
        }
        finally
        {
            if (item is not null)
                Marshal.ReleaseComObject(item);
            Marshal.Release(pointer);
        }
    }

    /// <summary>
    /// Handwritten: CsWin32's declaration takes an <c>IBindCtx</c>, whose generated monikers use the banned
    /// <c>System.Runtime.InteropServices.ComTypes.FILETIME</c>.
    /// </summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);
}
