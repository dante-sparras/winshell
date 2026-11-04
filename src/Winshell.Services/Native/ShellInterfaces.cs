using System.Runtime.InteropServices;
using System.Text;

namespace Winshell.Native;

/// <summary>IShellItem. Only <c>GetDisplayName</c> is used; the two methods before it keep it on its vtable slot.</summary>
[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler();

    void GetParent();

    [PreserveSig]
    int GetDisplayName(int sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
}

/// <summary>IShellLinkW, with the Windows SDK's methods in its vtable order.</summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint findData, uint flags);

    void GetIDList(out nint idList);

    void SetIDList(nint idList);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

    void GetHotkey(out short hotkey);

    void SetHotkey(short hotkey);

    void GetShowCmd(out int showCmd);

    void SetShowCmd(int showCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int max, out int icon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

    void Resolve(nint hwnd, uint flags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}
