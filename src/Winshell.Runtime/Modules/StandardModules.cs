using Winshell.Runtime.QtQml;

namespace Winshell.Runtime;

/// <summary>Registers the built-in QML modules.</summary>
public static partial class StandardModules
{
    /// <summary>Extra registrations, such as the platform's modules, run after the built-in ones.</summary>
    public static event Action<ModuleRegistry>? Registering;

    public static ModuleRegistry Create()
    {
        var registry = new ModuleRegistry();
        RegisterQtQml(registry.Module("QtQml"));
        RegisterQtQml(registry.Module("QtQml.Models"));
        RegisterQuick(registry);
        RegisterShell(registry);
        Registering?.Invoke(registry);
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
