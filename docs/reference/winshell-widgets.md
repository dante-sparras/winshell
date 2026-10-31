# Winshell.Widgets

`import Winshell.Widgets`. The module has small building blocks for shell surfaces: an icon image that stays sharp at every scale, rectangles that clip their children to rounded corners, and wrappers that size themselves to one child plus margins.

## IconImage

A square image for an icon, created with `IconImage { }`. It loads the image at its own size times the window's scale, so an icon stays sharp on every display, and fits it inside the item with its aspect ratio kept. It is an Item.

| Member | Type | Description |
| --- | --- | --- |
| `source` | string | The image, as for `Image.source`: a path, a URL, or an `image://` source such as `image://icon/...`. A relative path is relative to the QML file. |
| `implicitSize` | real | Sets both `implicitWidth` and `implicitHeight`. Default `0`. |
| `actualSize` | size, read-only | The item's current width and height. |
| `asynchronous` | bool | Loads the image off the UI thread. Default `false`. |
| `mipmap` | bool | Smooths the image when it is drawn smaller than it was loaded. Default `false`. |
| `status` | int, read-only | The image's load status: `Image.Null`, `Image.Ready`, `Image.Loading` or `Image.Error`. |
| `backer` | Image, read-only | The `Image` inside that draws the icon. |

## ClippingRectangle

A Rectangle whose children are clipped to its rounded corners, created with `ClippingRectangle { }`. It has the members of a Rectangle, and `clip` is on.

| Member | Type | Description |
| --- | --- | --- |
| `contentItem` | Item, read-only | The item children go in: the rectangle itself. |
| `contentInsideBorder` | bool | Clips the children inside the border, when the border is visible. Default `false`, which clips them to the outer edge, so they can cover the border. |

## WrapperItem

An Item sized to one child plus margins, created with `WrapperItem { }`. The first item declared inside it becomes `child`, unless `child` is set. The wrapper's implicit size is the child's implicit size plus the margins, and the child is placed inside the margins. A hidden child counts as size zero.

| Member | Type | Description |
| --- | --- | --- |
| `child` | Item | The wrapped item. Setting it to an item declared elsewhere moves that item into the wrapper. |
| `margin` | real | The space on every side. Default `0`. |
| `topMargin` | real | The space above the child. Follows `margin` until set. |
| `bottomMargin` | real | The space below the child. Follows `margin` until set. |
| `leftMargin` | real | The space left of the child. Follows `margin` until set. |
| `rightMargin` | real | The space right of the child. Follows `margin` until set. |
| `extraMargin` | real | Added to each of the four margins. Default `0`. |
| `resizeChild` | bool | Default `true`: the child fills the wrapper inside the margins. False keeps the child at its implicit size, shrunk to fit, and splits the free space in the ratio of the margins, so equal margins center it. |

Setting `implicitWidth` or `implicitHeight` on a wrapper replaces the size made from the child.

## WrapperRectangle

A Rectangle sized to one child plus margins, created with `WrapperRectangle { }`. It has the members of a Rectangle, and the wrapper members and behavior of [WrapperItem](#wrapperitem).

| Member | Type | Description |
| --- | --- | --- |
| `child` | Item | The wrapped item. Setting it to an item declared elsewhere moves that item into the wrapper. |
| `margin` | real | The space on every side. Default `0`. |
| `topMargin` | real | The space above the child. Follows `margin` until set. |
| `bottomMargin` | real | The space below the child. Follows `margin` until set. |
| `leftMargin` | real | The space left of the child. Follows `margin` until set. |
| `rightMargin` | real | The space right of the child. Follows `margin` until set. |
| `extraMargin` | real | Added to each of the four margins. Default `0`. |
| `resizeChild` | bool | Default `true`: the child fills the rectangle inside the margins. False keeps the child at its implicit size and places it by the ratio of the margins. |
| `contentInsideBorder` | bool | When `true` (the default) and the border is visible, each margin grows by the border's width, so the child stays inside the border. `false` keeps the margins as they are. |

```qml
import QtQuick
import Winshell
import Winshell.Theme
import Winshell.Widgets

FloatingWindow {
    implicitWidth: badge.implicitWidth
    implicitHeight: badge.implicitHeight
    color: "transparent"

    WrapperRectangle {
        id: badge
        margin: 8
        leftMargin: 12
        rightMargin: 12
        radius: Theme.controlCornerRadius
        color: Theme.accentFillColorDefault

        Text {
            text: "3 updates"
            color: Theme.textOnAccentFillColorPrimary
            font: Theme.typography.body
        }
    }
}
```

## ClippingWrapperRectangle

A [WrapperRectangle](#wrapperrectangle) that clips its child to its rounded corners, created with `ClippingWrapperRectangle { }`. It has the members of [WrapperRectangle](#wrapperrectangle), and `clip` is on. The clip follows the rectangle's outer edge.

| Member | Type | Description |
| --- | --- | --- |

## WrapperMouseArea

A MouseArea sized to one child plus margins, created with `WrapperMouseArea { }`. It has the members of a MouseArea, and the wrapper members and behavior of [WrapperItem](#wrapperitem), so a click anywhere in the margins counts.

| Member | Type | Description |
| --- | --- | --- |
| `child` | Item | The wrapped item. Setting it to an item declared elsewhere moves that item into the wrapper. |
| `margin` | real | The space on every side. Default `0`. |
| `topMargin` | real | The space above the child. Follows `margin` until set. |
| `bottomMargin` | real | The space below the child. Follows `margin` until set. |
| `leftMargin` | real | The space left of the child. Follows `margin` until set. |
| `rightMargin` | real | The space right of the child. Follows `margin` until set. |
| `extraMargin` | real | Added to each of the four margins. Default `0`. |
| `resizeChild` | bool | Default `true`: the child fills the area inside the margins. False keeps the child at its implicit size and places it by the ratio of the margins. |

## MarginWrapperManager

Gives the item it is declared in the margin behavior of a wrapper, created with `MarginWrapperManager { }` inside that item. The item's implicit size becomes the child's plus the margins, and the child is placed inside them. Use it to make your own wrapper type.

| Member | Type | Description |
| --- | --- | --- |
| `wrapper` | Item, read-only | The item the manager is declared in. |
| `child` | Item | The wrapped item. Default: the first item child of the wrapper when the manager is complete. |
| `margin` | real | The space on every side. Default `0`. |
| `topMargin` | real | The space above the child. Follows `margin` until set. |
| `bottomMargin` | real | The space below the child. Follows `margin` until set. |
| `leftMargin` | real | The space left of the child. Follows `margin` until set. |
| `rightMargin` | real | The space right of the child. Follows `margin` until set. |
| `extraMargin` | real | Added to each of the four margins. Default `0`. |
| `resizeChild` | bool | Default `true`: the child fills the wrapper inside the margins. False keeps the child at its implicit size and places it by the ratio of the margins. |
| `implicitWidth` | real | Overrides the item's implicit width. Until it is set, that width is the child's plus the margins. |
| `implicitHeight` | real | Overrides the item's implicit height. Until it is set, that height is the child's plus the margins. |

The manager's properties do not emit change signals, so a binding that reads them does not update.

## WrapperManager

Picks the one visual child of the item it is declared in, created with `WrapperManager { }` inside that item. With one item child, that child is used. With more, set `child`, and the others are taken out of the item. With more and no `child`, all of them are taken out and a warning is logged in the `scene` category. Children added later are not picked. It does no sizing; see [MarginWrapperManager](#marginwrappermanager) for margins.

| Member | Type | Description |
| --- | --- | --- |
| `wrapper` | Item | The item whose child is picked. Default: the item the manager is declared in. It can only be set before the manager is complete. |
| `child` | Item | The picked child. Setting another item moves it into the wrapper and takes the previous one out. It turns `null` when the child is destroyed. |
