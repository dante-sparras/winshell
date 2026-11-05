using System.Runtime.InteropServices;

namespace Winshell.Native;

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    [PreserveSig] int QueryService(in Guid service, in Guid iid, out nint instance);
}

/// <summary>
/// The internal desktop manager on builds 26100 through 26399. Slots Windows inserted in 24H2 stay, including
/// <c>MoveDesktop</c>, which is not called: removing it would move <c>RemoveDesktop</c> onto the wrong slot.
/// </summary>
[ComImport]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManagerInternal
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int MoveViewToDesktop(nint view, nint desktop);
    [PreserveSig] int CanViewMoveDesktops(nint view, out int canMove);
    [PreserveSig] int GetCurrentDesktop(out nint desktop);
    [PreserveSig] int GetDesktops(out nint desktops);
    [PreserveSig] int GetAdjacentDesktop(nint desktop, int direction, out nint adjacent);
    [PreserveSig] int SwitchDesktop(nint desktop);
    [PreserveSig] int SwitchDesktopAndMoveForegroundView(nint desktop);
    [PreserveSig] int CreateDesktop(out nint desktop);
    [PreserveSig] int MoveDesktop(nint desktop, int index);
    [PreserveSig] int RemoveDesktop(nint destroy, nint fallback);
}

[ComImport]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktop
{
    [PreserveSig] int IsViewVisible(nint view, out int visible);
    [PreserveSig] int GetId(out Guid id);
    [PreserveSig] int GetName(out nint name);
}

[ComImport]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object item);
}

[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, out int onCurrent);
    [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid desktopId);
    [PreserveSig] int MoveWindowToDesktop(nint hwnd, in Guid desktopId);
}
