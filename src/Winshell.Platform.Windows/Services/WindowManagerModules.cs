using Winshell.Platform.Windows.Services.Apps;
using Winshell.Platform.Windows.Services.Winland;
using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services;

/// <summary>
/// The window and app modules: <c>Apps</c> and <c>FocusGrab</c> in <c>Winshell</c>, <c>Winshell.Windows</c> on the
/// taskbar's windows, and <c>Winshell.Winland</c> on Winland.
/// </summary>
internal static class WindowManagerModules
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Winshell")
            .Singleton(e => new AppsService(e), "Apps")
            .Uncreatable<AppEntry>("AppEntry")
            .Uncreatable<AppAction>("AppAction")
            .Type<FocusGrab>("FocusGrab");

        registry.Module("Winshell.Windows")
            .Singleton(e => new WindowListService(e), "WindowList")
            .Uncreatable<AppWindow>("AppWindow");

        registry.Module("Winshell.Winland")
            .Singleton(e => new WinlandService(e), "Winland")
            .Uncreatable<WinlandEvent>("WinlandEvent")
            .Uncreatable<WinlandMonitor>("WinlandMonitor")
            .Uncreatable<WinlandWorkspace>("WinlandWorkspace")
            .Uncreatable<WinlandWindow>("WinlandWindow", "WinlandWindow comes from Winland.toplevels, or as an attached object of an AppWindow", attached: WinlandWindow.Attach, attachedType: typeof(WinlandWindow));
    }
}
