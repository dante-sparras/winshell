using System.Runtime.InteropServices;

namespace Winshell.Native;

internal static class ShellItemName
{
    /// <summary>
    /// The name the shell shows for the item at <paramref name="parsingName"/>, such as
    /// <c>shell:AppsFolder\{AppUserModelID}</c>, or null when it has no such item.
    /// </summary>
    internal static string? Find(string parsingName)
    {
        if (Win32.SHCreateItemFromParsingName(parsingName, 0, typeof(IShellItem).GUID, out var pointer) < 0 || pointer == 0)
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
}
