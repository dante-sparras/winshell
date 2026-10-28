using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;
using Effects = Winshell.Runtime.Quick.Effects;
using Shapes = Winshell.Runtime.Quick.Shapes;

namespace Winshell.Runtime;

public static partial class StandardModules
{
    // Winshell has one control style, Fluent. Configs written for another Qt style get it too.
    private static readonly string[] ControlsModules =
    [
        "QtQuick.Controls", "QtQuick.Controls.Basic", "QtQuick.Controls.FluentWinUI3", "QtQuick.Controls.Fusion",
        "QtQuick.Controls.Material", "QtQuick.Controls.Universal", "QtQuick.Controls.Imagine",
    ];

    private static void RegisterControlsModules(ModuleRegistry registry)
    {
        registry.Module("QtQuick")
            .Type<Quick.Canvas.Canvas>("Canvas")
            .Type<Quick.Canvas.TextMetrics>("TextMetrics")
            .Uncreatable<Quick.Canvas.Context2D>("Context2D")
            .Uncreatable<Quick.Canvas.CanvasGradient>("CanvasGradient")
            .Type<Shapes.PathMove>("PathMove")
            .Type<Shapes.PathLine>("PathLine")
            .Type<Shapes.PathQuad>("PathQuad")
            .Type<Shapes.PathCubic>("PathCubic")
            .Type<Shapes.PathArc>("PathArc")
            .Type<Shapes.PathAngleArc>("PathAngleArc")
            .Type<Shapes.PathSvg>("PathSvg")
            .Type<Shapes.PathRectangle>("PathRectangle")
            .Type<Shapes.PathPolyline>("PathPolyline");

        foreach (var name in ControlsModules)
            RegisterControls(registry.Module(name));

        registry.Module("QtQuick.Shapes")
            .Type<Shapes.Shape>("Shape")
            .Type<Shapes.ShapePath>("ShapePath")
            .Uncreatable<Shapes.ShapeGradient>("ShapeGradient")
            .Type<Shapes.LinearGradient>("LinearGradient")
            .Type<Shapes.RadialGradient>("RadialGradient")
            .Type<Shapes.ConicalGradient>("ConicalGradient");

        registry.Module("QtQuick.Effects")
            .Type<Effects.MultiEffect>("MultiEffect")
            .Type<Effects.RectangularShadow>("RectangularShadow");

        foreach (var name in new[] { "Qt5Compat.GraphicalEffects", "QtGraphicalEffects" })
        {
            registry.Module(name)
                .Type<Effects.DropShadow>("DropShadow")
                .Type<Effects.Glow>("Glow")
                .Type<Effects.RectangularGlow>("RectangularGlow")
                .Type<Effects.ColorOverlay>("ColorOverlay")
                .Type<Effects.OpacityMask>("OpacityMask")
                .Type<Effects.GaussianBlur>("GaussianBlur")
                .Type<Effects.FastBlur>("FastBlur")
                .Type<Effects.Desaturate>("Desaturate")
                .Type<Effects.BrightnessContrast>("BrightnessContrast")
                .Type<Effects.LinearGradient>("LinearGradient")
                .Type<Effects.RadialGradient>("RadialGradient")
                .Type<Effects.ConicalGradient>("ConicalGradient");
        }
    }

    private static void RegisterControls(QmlModule controls) =>
        controls.Type<Control>("Control")
            .Type<Label>("Label")
            .Type<TextField>("TextField")
            .Type<TextArea>("TextArea")
            .Uncreatable<AbstractButton>("AbstractButton")
            .Type<Button>("Button")
            .Type<RoundButton>("RoundButton")
            .Type<ToolButton>("ToolButton")
            .Type<CheckBox>("CheckBox")
            .Type<RadioButton>("RadioButton")
            .Type<Switch>("Switch")
            .Type<ItemDelegate>("ItemDelegate")
            .Type<TabButton>("TabButton")
            .Type<ButtonGroup>("ButtonGroup", attached: o => new ButtonGroupAttached(o), attachedType: typeof(ButtonGroupAttached))
            .Type<Pane>("Pane")
            .Type<Frame>("Frame")
            .Type<GroupBox>("GroupBox")
            .Type<ToolBar>("ToolBar")
            .Type<ToolSeparator>("ToolSeparator")
            .Type<Page>("Page")
            .Type<ApplicationWindow>("ApplicationWindow")
            .Uncreatable<Container>("Container")
            .Type<TabBar>("TabBar")
            .Type<SwipeView>("SwipeView", attached: o => new SwipeViewAttached(o), attachedType: typeof(SwipeViewAttached))
            .Type<StackView>("StackView", attached: o => new StackViewAttached(o), attachedType: typeof(StackViewAttached))
            .Type<Popup>("Popup")
            .Uncreatable<OverlayAttached>("Overlay", "Overlay is only available via attached properties", attached: o => new OverlayAttached(o), attachedType: typeof(OverlayAttached))
            .Type<ToolTip>("ToolTip", attached: o => new ToolTipAttached(o), attachedType: typeof(ToolTipAttached))
            .Type<Menu>("Menu")
            .Type<MenuItem>("MenuItem")
            .Type<MenuSeparator>("MenuSeparator")
            .Type<ComboBox>("ComboBox")
            .Type<Slider>("Slider")
            .Type<RangeSlider>("RangeSlider")
            .Type<ProgressBar>("ProgressBar")
            .Type<BusyIndicator>("BusyIndicator")
            .Type<Dial>("Dial")
            .Type<SpinBox>("SpinBox")
            .Type<ScrollBar>("ScrollBar", attached: o => new ScrollBarAttached(o), attachedType: typeof(ScrollBarAttached))
            .Type<ScrollView>("ScrollView")
            .Type<ScrollIndicator>("ScrollIndicator");
}
