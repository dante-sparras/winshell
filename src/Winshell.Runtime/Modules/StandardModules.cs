using Winshell.Runtime.QtQml;

namespace Winshell.Runtime;

/// <summary>Registers the built-in QML modules.</summary>
public static partial class StandardModules
{
    /// <summary>
    /// One extra registration, such as the platform's modules, run after the built-in ones.
    /// Assigning replaces the previous action, so repeated installs do not stack.
    /// </summary>
    public static Action<ModuleRegistry>? ExtraModules { get; set; }

    public static ModuleRegistry Create()
    {
        var registry = new ModuleRegistry();
        RegisterQtQml(registry.Module("QtQml"));
        // QtQml.Models is only the model types. QtObject and the other QtQml types stay in QtQml.
        RegisterQuick(registry);
        RegisterShell(registry);
        ExtraModules?.Invoke(registry);
        return registry;
    }

    internal static void RegisterQtQml(QmlModule m)
    {
        m.Type<QtObject>("QtObject")
            .Type<QtQml.Timer>("Timer")
            .Type<Connections>("Connections")
            .Type<BindingElement>("Binding")
            .Type<QmlComponent>("Component", attached: _ => new ComponentAttached(), attachedType: typeof(ComponentAttached));
    }

    static partial void RegisterQuickModules(ModuleRegistry registry);

    private static void RegisterQuick(ModuleRegistry registry)
    {
        var quick = registry.Module("QtQuick");
        RegisterQtQml(quick);
        RegisterQuickModules(registry);
    }
}
