# Qt Quick

Winshell runs the QML language itself, so the Qt Quick modules are Winshell's own implementation of them, not Qt. They behave as in Qt 6.10, and [Qt's documentation](https://doc.qt.io/qt-6/qtquick-index.html) describes their members. This page lists the types Winshell has. A type that isn't listed isn't there, and using it is a load error that names it.

Winshell draws Windows 11 controls whichever Qt style a configuration imports. `QtQuick.Controls.Basic`, `.Fusion`, `.Material`, `.Universal`, `.Imagine` and `.FluentWinUI3` all have the same types as `QtQuick.Controls`, drawn with the [theme](winshell-theme.md)'s Fluent tokens. Text is drawn with DirectWrite, and the built-in controls are exposed to UI Automation, so Narrator can read and use them ([ADR-0005](../adr/0005-directwrite-text-and-uia.md)).

## QtQml

`import QtQml`, also in `QtQml.Models`: `Binding`, `Component`, `Connections`, `QtObject`, `Timer`. `QtQml.Models` adds `ListModel` and `ListElement`.

## QtQuick

`import QtQuick`.

| Kind | Types |
| --- | --- |
| Items | `Item`, `Rectangle`, `Text`, `TextEdit`, `TextInput`, `TextMetrics`, `Image`, `AnimatedImage`, `Canvas`, `Loader`, `Flickable`, `ListView`, `Repeater` |
| Positioners | `Row`, `Column`, `Grid`, `Flow` |
| Input | `MouseArea`, `TapHandler`, `HoverHandler`, `DragHandler`, `WheelHandler` |
| Animation | `Behavior`, `PropertyAnimation`, `NumberAnimation`, `ColorAnimation`, `RotationAnimation`, `SmoothedAnimation`, `SpringAnimation`, `PauseAnimation`, `PropertyAction`, `ScriptAction`, `SequentialAnimation`, `ParallelAnimation` |
| States | `State`, `PropertyChanges`, `Transition` |
| Paint | `Gradient`, `GradientStop`, and for `Canvas` the `Context2D` and `CanvasGradient` objects |
| Paths | `PathMove`, `PathLine`, `PathPolyline`, `PathQuad`, `PathCubic`, `PathArc`, `PathAngleArc`, `PathRectangle`, `PathSvg` |
| Models | `ListModel`, `ListElement` |
| Attached and value types | `Keys`, `Accessible`, `Font`, `Easing` |
| From QtQml | `Binding`, `Component`, `Connections`, `QtObject`, `Timer` |

## QtQuick.Controls

`import QtQuick.Controls`.

| Kind | Types |
| --- | --- |
| Buttons | `AbstractButton`, `Button`, `RoundButton`, `ToolButton`, `TabButton`, `CheckBox`, `RadioButton`, `Switch`, `ButtonGroup`, `ItemDelegate` |
| Ranges | `Slider`, `RangeSlider`, `Dial`, `SpinBox`, `ProgressBar`, `BusyIndicator` |
| Text | `Label`, `TextField`, `TextArea` |
| Containers | `Control`, `Container`, `Frame`, `GroupBox`, `Pane`, `Page`, `ToolBar`, `ToolSeparator`, `TabBar`, `StackView`, `SwipeView`, `ScrollView`, `ScrollBar`, `ScrollIndicator`, `ApplicationWindow` |
| Popups | `Popup`, `Menu`, `MenuItem`, `MenuSeparator`, `ToolTip`, `ComboBox`, `Overlay` |

A `Popup` is drawn in its window's overlay; it isn't a window of its own. For a separate window next to another one, use a [PopupWindow](winshell.md#popupwindow).

## QtQuick.Layouts

`import QtQuick.Layouts`: `RowLayout`, `ColumnLayout`, `GridLayout`, `StackLayout`, and the attached `Layout`.

## QtQuick.Shapes

`import QtQuick.Shapes`: `Shape`, `ShapePath`, `LinearGradient`, `RadialGradient`, `ConicalGradient`, and their base `ShapeGradient`.

## QtQuick.Effects

`import QtQuick.Effects`: `MultiEffect` and `RectangularShadow`.

## Qt5Compat.GraphicalEffects

`import Qt5Compat.GraphicalEffects`, also as `QtGraphicalEffects`: `BrightnessContrast`, `ColorOverlay`, `ConicalGradient`, `Desaturate`, `DropShadow`, `FastBlur`, `GaussianBlur`, `Glow`, `LinearGradient`, `OpacityMask`, `RadialGradient`, `RectangularGlow`.

## The Qt object

The global `Qt` object has the helpers configurations use most, such as `Qt.rgba()`, `Qt.point()`, `Qt.rect()`, `Qt.size()`, `Qt.binding()` and the alignment and key enums. JavaScript runs on [Jint](https://github.com/sebastienros/jint) ([ADR-0001](../adr/0001-own-qml-parser-and-jint-runtime.md)).
