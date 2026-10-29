import Winshell.Theme
import QtQuick
import QtQuick.Controls

// A quick settings tile as Windows 11 draws it: a 96 by 48 button with a glyph, filled with the accent color while
// it is on, and its name under it. A tile with a page of its own shows a chevron. While quick settings is being
// edited, the tile has an unpin badge, can be dragged, and moves with Alt+Shift and the arrow keys.
Item {
  id: tile

  property string glyph
  // The tile's name. The label under it can show more, such as the network it is on.
  property string title
  property string label
  property string description
  property bool on
  // Whether the tile shows something that is on or off, so Narrator reads it as a toggle.
  property bool toggle
  property bool hasPage
  property bool editing
  // Where a dragged tile would land: -1 before this tile, 1 after it, 0 not here.
  property int dropSide: 0

  signal clicked()
  signal unpinClicked(bool fromKeyboard)
  // Moving by one tile to the side, or by a row with -3 and 3.
  signal moveRequested(int offset)
  signal dragStarted()
  signal dragMoved(real x, real y)
  signal dragEnded()

  function focusButton() {
    button.forceActiveFocus()
  }

  implicitWidth: 96
  implicitHeight: button.height + 6 + name.implicitHeight

  Button {
    id: button
    objectName: "tileButton"
    width: 96
    height: 48
    padding: 0
    text: tile.label
    highlighted: tile.on
    Accessible.checkable: tile.toggle && !tile.editing
    Accessible.checked: tile.on
    Accessible.description: tile.editing ? "Alt+Shift+arrow keys move it, Delete unpins it" : tile.description
    onClicked: if (!tile.editing) tile.clicked()

    Keys.onPressed: event => {
      // Keys handlers accept by default; leave other keys, such as Escape, to quick settings.
      event.accepted = false
      if (!tile.editing)
        return
      const alt = (event.modifiers & Qt.AltModifier) !== 0 && (event.modifiers & Qt.ShiftModifier) !== 0
      const offset = !alt ? 0 : event.key === Qt.Key_Left ? -1 : event.key === Qt.Key_Right ? 1 : event.key === Qt.Key_Up ? -3 : event.key === Qt.Key_Down ? 3 : 0
      if (offset !== 0) {
        tile.moveRequested(offset)
        event.accepted = true
      } else if (event.key === Qt.Key_Delete) {
        tile.unpinClicked(true)
        event.accepted = true
      }
    }

    contentItem: Item {
      Text {
        anchors.centerIn: parent
        text: tile.glyph
        font.family: Theme.iconFontFamily
        font.pixelSize: 16
        color: tile.on ? Theme.textOnAccentFillColorPrimary : Theme.textFillColorPrimary
      }

      Text {
        visible: tile.hasPage
        anchors.right: parent.right
        anchors.rightMargin: 10
        anchors.verticalCenter: parent.verticalCenter
        text: ""
        font.family: Theme.iconFontFamily
        font.pixelSize: 12
        color: tile.on ? Theme.textOnAccentFillColorPrimary : Theme.textFillColorPrimary
      }
    }
  }

  // Takes the pointer from the button while editing, so a press starts a drag instead of a click.
  MouseArea {
    objectName: "dragArea"
    visible: tile.editing
    anchors.fill: button
    onPressed: tile.dragStarted()
    onPositionChanged: mouse => tile.dragMoved(mouse.x, mouse.y)
    onReleased: tile.dragEnded()
    onCanceled: tile.dragEnded()
  }

  Rectangle {
    visible: tile.dropSide !== 0
    x: tile.dropSide < 0 ? -7 : button.width + 5
    width: 2
    height: button.height
    radius: 1
    color: Theme.accentFillColorDefault
  }

  Button {
    objectName: "unpin"
    visible: tile.editing
    x: button.width - 14
    y: -6
    width: 20
    height: 20
    padding: 0
    text: "Unpin " + tile.title
    onClicked: tile.unpinClicked(false)

    background: Rectangle {
      radius: 10
      color: Theme.controlSolidFillColorDefault
      border.width: 1
      border.color: Theme.controlStrokeColorDefault
    }

    contentItem: Text {
      horizontalAlignment: Text.AlignHCenter
      verticalAlignment: Text.AlignVCenter
      text: ""
      font.family: Theme.iconFontFamily
      font.pixelSize: 10
      color: Theme.textFillColorPrimary
    }
  }

  Text {
    id: name
    anchors.top: button.bottom
    anchors.topMargin: 6
    width: parent.width
    horizontalAlignment: Text.AlignHCenter
    elide: Text.ElideRight
    text: tile.label
    font.pixelSize: 12
    color: Theme.textFillColorPrimary
    // The button already carries the name.
    Accessible.ignored: true
  }
}
