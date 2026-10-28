using Winshell.Runtime.Quick;
using Winshell.Runtime.Quickshell;

namespace Winshell.Runtime;

public static partial class StandardModules
{
    /// <summary>Registers Quickshell, Quickshell.Io and Quickshell.Widgets.</summary>
    public static void RegisterQuickshell(ModuleRegistry registry)
    {
        var qs = registry.Module("Quickshell");
        qs.Singleton(e => new QuickshellGlobal(e), "Quickshell")
            .Type<ShellRoot>("ShellRoot")
            .Uncreatable<Reloadable>("Reloadable", "Reloadable is a base class")
            .Uncreatable<QuickshellSettings>("QuickshellSettings", "QuickshellSettings is ShellRoot.settings")
            .Type<Scope>("Scope")
            .Type<Singleton>("Singleton")
            .Type<Variants>("Variants")
            .Type<LazyLoader>("LazyLoader")
            .Type<PersistentProperties>("PersistentProperties")
            .Type<SystemClock>("SystemClock")
            .Type<EasingCurve>("EasingCurve")
            .Type<ColorQuantizer>("ColorQuantizer")
            .Type<TransformWatcher>("TransformWatcher")
            .Type<BoundComponent>("BoundComponent")
            .Uncreatable<RetainableHook>("Retainable", "Retainable can only be used as an attached object.", attached: RetainableHook.Attach, attachedType: typeof(RetainableHook))
            .Type<RetainableLock>("RetainableLock")
            .Uncreatable<QsMenuHandle>("QsMenuHandle")
            .Uncreatable<QsMenuEntry>("QsMenuEntry")
            .Uncreatable<QsMenuButtonType>("QsMenuButtonType")
            .Type<QsMenuOpener>("QsMenuOpener")
            .Type<QsMenuAnchor>("QsMenuAnchor")
            .Type<ElapsedTimer>("ElapsedTimer")
            .Type<ScriptModel>("ScriptModel")
            .Uncreatable<ObjectModel>("ObjectModel")
            .Type<Region>("Region")
            .Type<PanelWindow>("PanelWindow")
            .Type<FloatingWindow>("FloatingWindow")
            .Type<PopupWindow>("PopupWindow")
            .Uncreatable<ShellScreen>("ShellScreen")
            .Uncreatable<PopupAnchor>("PopupAnchor")
            .Uncreatable<QsWindowAttached>("QsWindow", "QsWindow is the base of Quickshell's windows", attached: o => new QsWindowAttached(o), attachedType: typeof(QsWindowAttached))
            .Uncreatable<Edges>("Edges")
            .Add(EnumType("ExclusionMode", [new("Normal", 0), new("Ignore", 1), new("Auto", 2)], qs.Name))
            .Add(EnumType("RegionShape", [new("Rect", 0), new("Ellipse", 1)], qs.Name))
            .Add(EnumType("Intersection", [new("Combine", 0), new("Subtract", 1), new("Intersect", 2), new("Xor", 3)], qs.Name))
            .Add(EnumType("ObjectComparison", [new("Structure", 0), new("Identity", 1)], qs.Name))
            .Add(EnumType("PopupAdjustment", [
                new("None", 0), new("SlideX", 1), new("SlideY", 2), new("Slide", 3), new("FlipX", 4), new("FlipY", 8), new("Flip", 12),
                new("ResizeX", 16), new("ResizeY", 32), new("Resize", 48), new("All", 63)], qs.Name));

        registry.Module("Quickshell.Wayland")
            .Type<WlSessionLock>("WlSessionLock")
            .Type<WlSessionLockSurface>("WlSessionLockSurface");

        var io = registry.Module("Quickshell.Io");
        io.Type<ProcessObject>("Process")
            .Type<StdioCollector>("StdioCollector")
            .Type<SplitParser>("SplitParser")
            .Uncreatable<DataStream>("DataStream", "DataStream is a base class")
            .Uncreatable<DataStreamParser>("DataStreamParser")
            .Type<FileView>("FileView")
            .Uncreatable<FileViewAdapter>("FileViewAdapter")
            .Type<JsonAdapter>("JsonAdapter")
            .Type<JsonObject>("JsonObject")
            .Type<IpcHandler>("IpcHandler")
            .Type<SocketObject>("Socket")
            .Type<SocketServer>("SocketServer")
            .Add(EnumType("FileViewError", [new("Success", 0), new("Unknown", 1), new("FileNotFound", 2), new("PermissionDenied", 3), new("NotAFile", 4)], io.Name));

        var widgets = registry.Module("Quickshell.Widgets");
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
