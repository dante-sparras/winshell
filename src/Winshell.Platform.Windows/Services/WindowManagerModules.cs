using Winshell.Platform.Windows.Services.DesktopEntries;
using Winshell.Platform.Windows.Services.Hyprland;
using Winshell.Platform.Windows.Services.Toplevels;
using Winshell.Runtime;
using Winshell.Runtime.Quickshell;

namespace Winshell.Platform.Windows.Services;

/// <summary>
/// The window manager and app modules: <c>Quickshell.Hyprland</c> on Winland, <c>ToplevelManager</c> in
/// <c>Quickshell.Wayland</c> on the taskbar's windows, and <c>DesktopEntries</c> in <c>Quickshell</c> on the Start menu.
/// </summary>
internal static class WindowManagerModules
{
    public static void Register(ModuleRegistry registry)
    {
        registry.Module("Quickshell")
            .Singleton(e => new DesktopEntries.DesktopEntries(e), "DesktopEntries")
            .Uncreatable<DesktopEntry>("DesktopEntry")
            .Uncreatable<DesktopAction>("DesktopAction");

        registry.Module("Quickshell.Wayland")
            .Singleton(e => new ToplevelManager(e), "ToplevelManager")
            .Uncreatable<Toplevel>("Toplevel");

        registry.Module("Quickshell.Hyprland")
            .Singleton(e => new HyprlandIpc(e), "Hyprland")
            .Uncreatable<HyprlandEvent>("HyprlandEvent")
            .Uncreatable<HyprlandMonitor>("HyprlandMonitor")
            .Uncreatable<HyprlandWorkspace>("HyprlandWorkspace")
            .Uncreatable<HyprlandToplevel>("HyprlandToplevel", "HyprlandToplevel comes from Hyprland.toplevels, or as an attached object of a Toplevel", attached: HyprlandToplevel.Attach, attachedType: typeof(HyprlandToplevel))
            .Uncreatable<HyprlandWindowAttached>("HyprlandWindow", "HyprlandWindow is only available as an attached object", attached: o => new HyprlandWindowAttached(o), attachedType: typeof(HyprlandWindowAttached))
            .Type<GlobalShortcut>("GlobalShortcut")
            .Type<HyprlandFocusGrab>("HyprlandFocusGrab");
    }
}
