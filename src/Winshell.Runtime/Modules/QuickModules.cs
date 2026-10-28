using Winshell.Runtime.Quick;

namespace Winshell.Runtime;

public static partial class StandardModules
{
    static partial void RegisterQuickModules(ModuleRegistry registry)
    {
        var quick = registry.Module("QtQuick");
        quick.Type<Item>("Item")
            .Type<Rectangle>("Rectangle")
            .Type<Text>("Text")
            .Type<Image>("Image")
            .Type<AnimatedImage>("AnimatedImage")
            .Type<MouseArea>("MouseArea")
            .Type<Row>("Row")
            .Type<Column>("Column")
            .Type<Grid>("Grid")
            .Type<Quick.Flow>("Flow")
            .Type<Repeater>("Repeater")
            .Type<Loader>("Loader")
            .Type<Flickable>("Flickable")
            .Type<ListView>("ListView")
            .Type<TextInput>("TextInput")
            .Type<TextEdit>("TextEdit")
            .Type<Gradient>("Gradient")
            .Type<GradientStop>("GradientStop")
            .Type<ListModel>("ListModel")
            .Type<ListElement>("ListElement")
            .Type<PropertyAnimation>("PropertyAnimation")
            .Type<NumberAnimation>("NumberAnimation")
            .Type<ColorAnimation>("ColorAnimation")
            .Type<RotationAnimation>("RotationAnimation")
            .Type<SmoothedAnimation>("SmoothedAnimation")
            .Type<SpringAnimation>("SpringAnimation")
            .Type<PauseAnimation>("PauseAnimation")
            .Type<ScriptAction>("ScriptAction")
            .Type<PropertyAction>("PropertyAction")
            .Type<SequentialAnimation>("SequentialAnimation")
            .Type<ParallelAnimation>("ParallelAnimation")
            .Type<Behavior>("Behavior")
            .Type<State>("State")
            .Type<PropertyChanges>("PropertyChanges")
            .Type<Transition>("Transition")
            .Type<HoverHandler>("HoverHandler")
            .Type<TapHandler>("TapHandler")
            .Type<WheelHandler>("WheelHandler")
            .Type<DragHandler>("DragHandler")
            .Uncreatable<KeysAttached>("Keys", "Keys is only available via attached properties", attached: _ => new KeysAttached(), attachedType: typeof(KeysAttached))
            .Add(EnumType("Easing", Easing.Enums, quick.Name))
            .Add(EnumType("Font", Enum.GetValues<FontGroup.Weights>().Select(w => new KeyValuePair<string, int>(w.ToString(), (int)w))
                .Concat([new("MixedCase", 0), new("AllUppercase", 1), new("AllLowercase", 2), new("SmallCaps", 3), new("Capitalize", 4)]), quick.Name));

        var models = registry.Module("QtQml.Models");
        models.Type<ListModel>("ListModel").Type<ListElement>("ListElement");

        var layouts = registry.Module("QtQuick.Layouts");
        layouts.Type<RowLayout>("RowLayout")
            .Type<ColumnLayout>("ColumnLayout")
            .Type<GridLayout>("GridLayout")
            .Type<StackLayout>("StackLayout")
            .Uncreatable<LayoutAttached>("Layout", "Layout is only available via attached properties", attached: o => new LayoutAttached(o), attachedType: typeof(LayoutAttached));

        // Parenting a created object to an item makes it a visual child.
        QmlParenting.VisualParent = (child, parent) =>
        {
            if (child is Item item && parent is Item parentItem)
            {
                item.SetParentItem(parentItem);
                return true;
            }
            if (parent is Item host && child is IItemContainerChild attachable)
            {
                attachable.AttachToItem(host);
                child.SetOwner(parent);
                return true;
            }
            return false;
        };
    }

    public static TypeEntry EnumType(string name, IEnumerable<KeyValuePair<string, int>> values, string module) => new()
    {
        Name = name,
        Module = module,
        ClrType = typeof(object),
        Creatable = false,
        Enums = values.ToDictionary(kv => kv.Key, kv => kv.Value),
    };
}
