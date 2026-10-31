using System.Runtime.InteropServices;
using System.Text;

namespace Winshell.Platform.Windows.Services.Apps;

/// <summary>
/// Reads another app's recent and frequent documents through <c>IApplicationDocumentLists</c>. That is the list
/// Windows will give. Pinned jump-list items and custom tasks are not read: <c>ICustomDestinationList</c> opens a
/// list so the caller can replace it, and Windows does not give another app's pinned list to a reader. This type
/// calls <c>SetAppID</c> and <c>GetList</c> only. It does not launch a destination or write the list.
/// The shell's UI thread is an STA, which this in-process class requires. A failed read is
/// <see cref="JumpListRead.Missing"/>, and the menu says the list cannot be read.
/// </summary>
internal sealed class WindowsJumpListReader : IJumpListReader
{
    public static readonly WindowsJumpListReader Instance = new();

    private static readonly Guid Clsid = new("86bec222-30f2-47e0-9f25-60d11cd75c28");
    private const uint ClsctxInprocServer = 1;
    private const uint Recent = 0;
    private const uint Frequent = 1;

    public JumpListRead Read(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
            return JumpListRead.Missing;
        var iid = typeof(IApplicationDocumentLists).GUID;
        var hr = CoCreateInstance(in Clsid, 0, ClsctxInprocServer, in iid, out var pointer);
        if (hr < 0 || pointer == 0)
            return JumpListRead.Missing;
        IApplicationDocumentLists lists;
        try
        {
            lists = (IApplicationDocumentLists)Marshal.GetObjectForIUnknown(pointer);
        }
        catch (InvalidCastException)
        {
            return JumpListRead.Missing;
        }
        finally
        {
            Marshal.Release(pointer);
        }
        try
        {
            if (lists.SetAppID(appUserModelId) < 0)
                return JumpListRead.Missing;
            return JumpListRead.From(Category(lists, Recent), Category(lists, Frequent));
        }
        finally
        {
            Marshal.ReleaseComObject(lists);
        }
    }

    private static IReadOnlyList<string>? Category(IApplicationDocumentLists lists, uint kind)
    {
        var arrayId = typeof(INameArray).GUID;
        if (lists.GetList(kind, JumpListRead.PerCategory, in arrayId, out var array) < 0 || array is null)
            return null;
        try
        {
            if (array.GetCount(out var count) < 0)
                return null;
            var names = new List<string>();
            var itemId = typeof(IShellItemName).GUID;
            var linkId = typeof(IShellLinkName).GUID;
            for (uint i = 0; i < count && names.Count < JumpListRead.PerCategory; i++)
            {
                var name = NameAt(array, i, itemId, linkId);
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
            return names;
        }
        finally
        {
            Marshal.ReleaseComObject(array);
        }
    }

    /// <summary>The name the jump list shows. A shell link that is not a shell item contributes its file name.</summary>
    private static string? NameAt(INameArray array, uint index, Guid itemId, Guid linkId)
    {
        if (array.GetAt(index, in itemId, out var item) >= 0 && item is not null)
        {
            try
            {
                if (item is IShellItemName shell && shell.GetDisplayName(0, out var name) >= 0 && !string.IsNullOrWhiteSpace(name))
                    return name;
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        if (array.GetAt(index, in linkId, out var link) >= 0 && link is not null)
        {
            try
            {
                if (link is IShellLinkName shellLink)
                {
                    var buffer = new StringBuilder(1024);
                    if (shellLink.GetPath(buffer, buffer.Capacity, 0, 0) >= 0)
                    {
                        var file = Path.GetFileName(buffer.ToString());
                        return string.IsNullOrWhiteSpace(file) ? null : file;
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
        return null;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [ComImport]
    [Guid("3c594f9f-9f30-47a1-979a-c9e83d3d0a06")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationDocumentLists
    {
        [PreserveSig]
        int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        [PreserveSig]
        int GetList(uint listType, uint count, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out INameArray? array);
    }

    [ComImport]
    [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INameArray
    {
        [PreserveSig] int GetCount(out uint count);

        [PreserveSig] int GetAt(uint index, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? item);
    }

    // The two methods before GetDisplayName keep that call on its vtable slot. They are not used.
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemName
    {
        void BindToHandler();

        void GetParent();

        [PreserveSig]
        int GetDisplayName(int sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkName
    {
        [PreserveSig]
        int GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, nint findData, uint flags);
    }
}
