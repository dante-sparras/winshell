# Qt Quick

Winshell runs the QML language itself, so the Qt Quick modules are Winshell's own implementation of them, not Qt. Qt 6.10 is the model: [Qt's documentation](https://doc.qt.io/qt-6/qtquick-index.html) describes their members, and most behave as it says. [Where Winshell differs from Qt](#where-winshell-differs-from-qt) lists the members that don't. This page lists the types Winshell has. A type that isn't listed isn't there, and using it is a load error that names it. A member Qt has that Winshell doesn't is a load error too, when a document sets it.

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

## Where Winshell differs from Qt

These members are there, so a document that uses them loads, but they don't do what Qt's documentation says ([#149](https://github.com/dante-sparras/winshell/issues/149)). Most are accepted and ignored. A test checks that each one names a real member; it can't tell when one starts working, so remove its row in the PR that makes it work.

| Member | What Winshell does |
| --- | --- |
| `Context2D.getImageData()` | Returns null. |
| `Context2D.createImageData()` | Returns null. |
| `Context2D.putImageData()` | Draws nothing. |
| `Context2D.createPattern()` | Makes a pattern from a color only; for an image it returns null. |
| `Context2D.fillText()` | Draws with the one typeface `font` names, without fallback or shaping: characters that typeface lacks don't draw, and scripts that need shaping, such as Arabic or Devanagari, draw wrong. `strokeText()` and `measureText()` work the same way. |
| `Canvas.loadImage()` | Loads local images only. An `http` or `https` image never loads, and `isImageError()` turns true. |
| `Shape.horizontalAlignment` | Ignored, as is `verticalAlignment`: content scaled by `fillMode` sits at the top left. |
| `ShapePath.fillItem` | Ignored. |
| `ShapePath.fillTransform` | Ignored. |
| `PathRectangle.bevel` | Ignored; corners are always rounded. |
| `MenuItem.subMenu` | Always null: menus don't nest. |
| `Menu.cascade` | Ignored. |
| `Menu.currentIndex` | Not updated by the highlight or the keys. |
| `Popup.enter` | Ignored, as is `exit`: popups open and close without a transition. |
| `StackView.busy` | Always false. |
| `StackView.pushEnter` | Ignored, as are `pushExit`, `popEnter`, `popExit`, `replaceEnter` and `replaceExit`: pages switch at once. |
| `StackView.activating` | Never emitted, nor is `deactivating`: `StackView.status` goes straight between inactive and active. |
| `ComboBox.editable` | Ignored: there is no edit field. `editText` isn't updated, `acceptableInput` is always true, `validator` isn't used, and `accepted` isn't emitted. |
| `ComboBox.selectAll()` | Does nothing. |
| `SpinBox.editable` | Ignored: the value can't be typed, and `valueFromText` isn't called. |
| `CheckBox.nextCheckStateFunction` | Never called. Qt names this `nextCheckState`. |
| `ApplicationWindow.menuBar` | Not shown. |
| `TextArea.lineCount` | Always 1. `TextArea` and `TextEdit` hold one line: Enter emits `accepted`, and pasted line breaks become spaces. |
| `TextEdit.lineCount` | Always 1, as for `TextArea`. |
| `TextArea.textFormat` | Ignored, as on `TextEdit`: text is plain. |
| `TextEdit.textFormat` | Ignored. |
| `Text.textFormat` | `Text.MarkdownText` shows the Markdown as plain text. |
| `Text.fontSizeMode` | Ignored: the text never shrinks to fit. `minimumPixelSize` and `minimumPointSize` aren't used. |
| `Text.linkActivated` | Never emitted, nor is `linkHovered`: links are colored but can't be clicked. |
| `Text.hoveredLink` | Always empty, as is what `linkAt()` returns. |
| `TextInput.validator` | Ignored; `acceptableInput` is always true. |
| `TextInput.inputMask` | Ignored. |
| `TextInput.redo()` | Does nothing. |
| `TextInput.cursorDelegate` | Ignored; the built-in caret is drawn. |
| `TextInput.overwriteMode` | Ignored, as is `wrapMode`. |
| `Flickable.flicking` | Always false. Content stops when the pointer lets go: `flickStarted` and `flickEnded` aren't emitted, `horizontalVelocity` and `verticalVelocity` are 0, and `flickDeceleration` and `maximumFlickVelocity` aren't used. |
| `Flickable.boundsBehavior` | Ignored: dragging stops at the bounds, with no overshoot. |
| `ListView.section` | Ignored: there are no section headers. |
| `ListView.snapMode` | Ignored. |
| `ListView.highlightRangeMode` | Ignored, as are `preferredHighlightBegin` and `preferredHighlightEnd`. |
| `ListView.highlightMoveDuration` | Ignored, as is `highlightMoveVelocity`: the highlight jumps to the current item. |
| `ListView.populate` | Ignored, as are `add`, `remove`, `move`, `displaced`, `addDisplaced`, `removeDisplaced` and `moveDisplaced`: there are no view transitions. The attached `add` and `remove` signals aren't emitted. |
| `Row.add` | Ignored, as are `move` and `populate`, here and on `Column`, `Grid` and `Flow`. |
| `AnimatedImage.frameCount` | Always 1: only the first frame is shown, and `playing`, `paused`, `currentFrame` and `speed` aren't used. |
| `Image.mirrorVertically` | Ignored. |
| `Image.sourceClipRect` | Ignored. |
| `GaussianBlur.cached` | Ignored, on every effect: the source is drawn and filtered again on each paint. |
| `Keys.forwardTo` | Ignored. A key event's `matches()` is always false. |
| `MouseArea.propagateComposedEvents` | Ignored. |
| `TapHandler.gesturePolicy` | Ignored: a release inside the item is a tap. |
| `TapHandler.timeHeld` | Always 0. |
| `TapHandler.acceptedModifiers` | Ignored, on every pointer handler. |
| `DragHandler.translation` | Always (0, 0). |
| `DragHandler.xAxis` | Ignored, as is `yAxis`: no axis lock or limits. |
| `State.extend` | Ignored. |
| `PropertyAnimation.alwaysRunToEnd` | Ignored, on every animation. |
| `SpringAnimation.spring` | Not a spring: the value eases out to its target over a fixed time that `spring` and `damping` set. There is no overshoot, and `mass`, `velocity`, `epsilon` and `modulus` are ignored. |
| `RowLayout.uniformCellSizes` | Ignored, as on `ColumnLayout` and `GridLayout`. |
| `Binding.delayed` | Ignored, as is `restoreMode`. |
| `Text.font` | `font.styleName`, `font.kerning`, `font.features` and `font.variableAxes` are ignored, here and on every item and control with a `font`. |
| `Loader.asynchronous` | Ignored. The component is still created on this thread before the property assignment returns. |

## The Qt object

The global `Qt` object has the helpers configurations use most, such as `Qt.rgba()`, `Qt.point()`, `Qt.rect()`, `Qt.size()`, `Qt.binding()` and the alignment and key enums. JavaScript runs on [Jint](https://github.com/sebastienros/jint) ([ADR-0001](../adr/0001-own-qml-parser-and-jint-runtime.md)).
