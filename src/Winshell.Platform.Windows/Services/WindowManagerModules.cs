using Winshell.Platform.Windows.Services.Apps;
using Winshell.Platform.Windows.Services.Winland;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services;

/// <summary>
/// The window modules: <c>FocusGrab</c> in <c>Winshell</c>, <c>Winshell.Apps</c> on the Start menu,
/// <c>Winshell.Windows</c> on the taskbar's windows, and <c>Winshell.Winland</c> on Winland.
/// </summary>
internal static class WindowManagerModules
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell")
            .Type<FocusGrab>("FocusGrab");

        registry.Module("Winshell.Apps")
            .Singleton(e => new AppsService(e), "Apps")
            .Uncreatable<AppEntry>("AppEntry", "AppEntry comes from Apps")
            .Singleton(e => new JumpListService(e), "JumpLists")
            .Uncreatable<AppJumpList>("AppJumpList", "AppJumpList comes from JumpLists.forApp")
            .Singleton(_ => new RecentDocuments(), "RecentDocuments");

        registry.Module("Winshell.Windows")
            .Singleton(e => new WindowListService(e), "WindowList")
            .Uncreatable<AppWindow>("AppWindow")
            .Type<WindowThumbnail>("WindowThumbnail")
            .Singleton(e => new VirtualDesktopsService(e), "VirtualDesktops")
            .Uncreatable<VirtualDesktop>("VirtualDesktop");

        registry.Module("Winshell.Winland")
            .Singleton(e => new WinlandService(e), "Winland")
            .Uncreatable<WinlandEvent>("WinlandEvent")
            .Uncreatable<WinlandMonitor>("WinlandMonitor")
            .Uncreatable<WinlandWorkspace>("WinlandWorkspace")
            .Uncreatable<WinlandWindow>("WinlandWindow", "WinlandWindow comes from Winland.toplevels");
    }
}
