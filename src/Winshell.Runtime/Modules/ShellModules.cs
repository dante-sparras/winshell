using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime;

public static partial class StandardModules
{
    /// <summary>Registers Winshell, Winshell.Io and Winshell.Widgets.</summary>
    public static void RegisterShell(ModuleRegistry registry)
    {
        var core = registry.Module("Winshell");
        core.Singleton(e => new ShellGlobal(e), "Shell")
            .Type<ShellRoot>("ShellRoot")
            .Uncreatable<Reloadable>("Reloadable", "Reloadable is a base class")
            .Uncreatable<ShellSettings>("ShellSettings", "ShellSettings is ShellRoot.settings")
            .Type<Scope>("Scope")
            .Type<Singleton>("Singleton")
            .Type<Variants>("Variants")
            .Type<LazyLoader>("LazyLoader")
            .Type<PersistentProperties>("PersistentProperties")
            .Type<SystemClock>("SystemClock")
            .Type<CalendarMonth>("CalendarMonth")
            .Uncreatable<CalendarDay>("CalendarDay")
            .Type<EasingCurve>("EasingCurve")
            .Type<ColorQuantizer>("ColorQuantizer")
            .Type<TransformWatcher>("TransformWatcher")
            .Type<BoundComponent>("BoundComponent")
            .Uncreatable<RetainableHook>("Retainable", "Retainable can only be used as an attached object.", attached: RetainableHook.Attach, attachedType: typeof(RetainableHook))
            .Type<RetainableLock>("RetainableLock")
            .Uncreatable<MenuHandle>("MenuHandle")
            .Uncreatable<MenuEntry>("MenuEntry")
            .Uncreatable<MenuButtonType>("MenuButtonType")
            .Type<MenuOpener>("MenuOpener")
            .Type<MenuAnchor>("MenuAnchor")
            .Type<ElapsedTimer>("ElapsedTimer")
            .Type<ScriptModel>("ScriptModel")
            .Uncreatable<ObjectModel>("ObjectModel")
            .Type<Region>("Region")
            .Type<PanelWindow>("PanelWindow")
            .Type<FloatingWindow>("FloatingWindow")
            .Type<PopupWindow>("PopupWindow")
            .Uncreatable<ShellScreen>("ShellScreen")
            .Uncreatable<PopupAnchor>("PopupAnchor")
            .Uncreatable<ShellWindowAttached>("ShellWindow", "ShellWindow is the base of the shell's windows", attached: o => new ShellWindowAttached(o), attachedType: typeof(ShellWindowAttached))
            .Uncreatable<Edges>("Edges")
            .Uncreatable<PanelWindow.ExclusionModeValue>("ExclusionMode")
            .Uncreatable<Region.ShapeValue>("RegionShape")
            .Uncreatable<Intersection>("Intersection")
            .Uncreatable<ObjectComparison>("ObjectComparison")
            .Uncreatable<PopupAdjustment>("PopupAdjustment");

        var io = registry.Module("Winshell.Io");
        io.Type<ProcessObject>("Process")
            .Type<StdioCollector>("StdioCollector")
            .Type<SplitParser>("SplitParser")
            .Uncreatable<DataStream>("DataStream", "DataStream is a base class")
            .Uncreatable<DataStreamParser>("DataStreamParser")
            .Type<FileView>("FileView")
            .Uncreatable<FileViewAdapter>("FileViewAdapter")
            .Type<JsonAdapter>("JsonAdapter")
            .Type<JsonObject>("JsonObject")
            .Type<TomlAdapter>("TomlAdapter")
            .Type<IpcHandler>("IpcHandler")
            .Type<SocketObject>("Socket")
            .Type<SocketServer>("SocketServer")
            .Uncreatable<FileView.FileViewError>("FileViewError");

        var widgets = registry.Module("Winshell.Widgets");
        widgets.Type<IconImage>("IconImage")
            .Type<ClippingRectangle>("ClippingRectangle")
            .Type<ClippingWrapperRectangle>("ClippingWrapperRectangle")
            .Type<WrapperItem>("WrapperItem")
            .Type<WrapperRectangle>("WrapperRectangle")
            .Type<WrapperMouseArea>("WrapperMouseArea")
            .Type<MarginWrapperManager>("MarginWrapperManager")
            .Type<WrapperManager>("WrapperManager");
    }
}
